using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Viora;

/// <summary>
/// Minimal local Discord IPC transport. No Discord OAuth, tokens, webhooks, or third-party NuGet packages.
/// See https://discord.com/developers/docs/topics/rpc#setactivity
/// </summary>
public sealed class DiscordPipeClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private string? _connectedApplicationId;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task PublishAsync(string appId, DiscordActivity? activity)
    {
        await _gate.WaitAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var cancellationToken = timeout.Token;
            try
            {
                if (_pipe is null || !_pipe.IsConnected || _connectedApplicationId != appId)
                    await ConnectAsync(appId, cancellationToken);

                var packet = new
                {
                    cmd = "SET_ACTIVITY",
                    nonce = Guid.NewGuid().ToString("N"),
                    args = new { pid = Environment.ProcessId, activity = activity?.ToRpcObject() }
                };
                await SendAsync(_pipe!, 1, JsonSerializer.Serialize(packet), cancellationToken);
                var (opcode, message) = await ReceiveAsync(_pipe!, cancellationToken);
                if (opcode == 2 || IsDiscordError(message))
                    throw new IOException("Discord rejected this Rich Presence update. Verify the Application ID and Discord activity settings.");
            }
            catch
            {
                ClosePipe();
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task ClearIfConnectedAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_pipe?.IsConnected == true && !string.IsNullOrEmpty(_connectedApplicationId))
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await SendAsync(_pipe, 1, JsonSerializer.Serialize(new
                    {
                        cmd = "SET_ACTIVITY", nonce = Guid.NewGuid().ToString("N"),
                        args = new { pid = Environment.ProcessId, activity = (object?)null }
                    }), timeout.Token);
                    await ReceiveAsync(_pipe, timeout.Token);
                }
                catch (Exception) { /* Closing the pipe also clears this application's presence. */ }
            }
            ClosePipe();
        }
        finally { _gate.Release(); }
    }

    private async Task ConnectAsync(string appId, CancellationToken ct)
    {
        ClosePipe();
        Exception? lastError = null;
        for (int index = 0; index < 10; index++)
        {
            var attempt = new NamedPipeClientStream(".", $"discord-ipc-{index}", PipeDirection.InOut,
                PipeOptions.Asynchronous);
            try
            {
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectTimeout.CancelAfter(TimeSpan.FromMilliseconds(240));
                await attempt.ConnectAsync(connectTimeout.Token);
                await SendAsync(attempt, 0, JsonSerializer.Serialize(new { v = 1, client_id = appId }), ct);
                var (opcode, reply) = await ReceiveAsync(attempt, ct);
                if (opcode != 1 || IsDiscordError(reply)) throw new IOException("Discord did not accept the handshake.");
                _pipe = attempt;
                _connectedApplicationId = appId;
                return;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
            {
                lastError = ex;
                attempt.Dispose();
                ct.ThrowIfCancellationRequested();
            }
        }
        throw new IOException("Discord desktop couldn't be reached. Open Discord, then try Save and connect.", lastError);
    }

    private static async Task SendAsync(Stream stream, int opcode, string json, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), payload.Length);
        await stream.WriteAsync(frame, ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<(int Opcode, string Message)> ReceiveAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, ct);
        var opcode = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        var size = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        if (size < 0 || size > 1024 * 1024) throw new InvalidDataException("Unexpected Discord IPC frame length.");
        var content = new byte[size];
        await stream.ReadExactlyAsync(content, ct);
        return (opcode, Encoding.UTF8.GetString(content));
    }

    private static bool IsDiscordError(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            return doc.RootElement.TryGetProperty("evt", out var evt) &&
                string.Equals(evt.GetString(), "ERROR", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
    }

    private void ClosePipe()
    {
        _pipe?.Dispose();
        _pipe = null;
        _connectedApplicationId = null;
    }

    public void Dispose() => ClosePipe();
}

public sealed class DiscordActivity
{
    public string Details { get; init; } = "";
    public string State { get; init; } = "";
    public long? StartUnixSeconds { get; init; }
    public string LargeImageKey { get; init; } = "";

    public object ToRpcObject()
    {
        var result = new Dictionary<string, object>
        {
            ["type"] = 0, // Playing
            ["details"] = Details.Length > 128 ? Details[..128] : Details,
            ["state"] = State.Length > 128 ? State[..128] : State
        };
        if (StartUnixSeconds is long start) result["timestamps"] = new { start };
        if (!string.IsNullOrWhiteSpace(LargeImageKey))
            result["assets"] = new { large_image = LargeImageKey, large_text = "Viora • Roblox companion" };
        return result;
    }
}
