using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace VioraManager;

public partial class ServerBrowserWindow : Window
{
    internal sealed class PublicServer
    {
        public string Id { get; init; } = "";
        public int Playing { get; init; }
        public int Capacity { get; init; }
        public string Title => "Server  ·  " + (Playing < Capacity ? "Joinable" : "Full");
        public string PopulationLabel => Playing + " / " + Capacity;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly ObservableCollection<PublicServer> _servers = new();
    private readonly long _placeId;
    private bool _busy;

    public string? SelectedJobId { get; private set; }

    public ServerBrowserWindow(long placeId)
    {
        InitializeComponent();
        _placeId = placeId;
        PlaceDescription.Text = "Public Roblox servers for Place ID " + placeId +
            ". Choose a server, or pick a random one from the returned page.";
        ServerList.ItemsSource = _servers;
        Loaded += async (_, _) => await LoadServersAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await LoadServersAsync();

    private async Task LoadServersAsync()
    {
        if (_busy) return;
        if (!int.TryParse(MinimumInput.Text.Trim(), out int min) ||
            !int.TryParse(MaximumInput.Text.Trim(), out int max) ||
            min < 0 || max < min || max > 1000)
        {
            MessageBox.Show(this, "Use valid player counts (min 0, max up to 1000).",
                "Server player filter", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _busy = true;
        _servers.Clear();
        ResultStatus.Text = "Loading public servers…";
        try
        {
            // Public Roblox list endpoint. No auth, cookies or account data.
            string url = "https://games.roblox.com/v1/games/" + _placeId +
                "/servers/Public?sortOrder=Asc&excludeFullGames=true&limit=100";
            using var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                ResultStatus.Text = "Roblox server list unavailable (HTTP " +
                    (int)response.StatusCode + "). Try again later.";
                return;
            }
            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync());
            if (!doc.RootElement.TryGetProperty("data", out var servers) ||
                servers.ValueKind != JsonValueKind.Array)
            {
                ResultStatus.Text = "Roblox server response was unexpected.";
                return;
            }

            int visible = 0;
            foreach (var item in servers.EnumerateArray())
            {
                string? id = item.TryGetProperty("id", out var serverId)
                    ? serverId.GetString() : null;
                if (id is null || !Guid.TryParse(id, out Guid guid)) continue;
                int count = item.TryGetProperty("playing", out var countNode)
                    ? countNode.GetInt32() : 0;
                int capacity = item.TryGetProperty("maxPlayers", out var capacityNode)
                    ? capacityNode.GetInt32() : 0;
                if (capacity <= 0 || count >= capacity || count < min || count > max)
                    continue;
                _servers.Add(new PublicServer
                {
                    Id = guid.ToString(), Playing = count, Capacity = capacity
                });
                visible++;
            }
            ResultStatus.Text = visible + " matching public servers on the first page returned by Roblox. " +
                "A changing server list does not guarantee successful joining.";
        }
        catch (Exception ex) when (ex is HttpRequestException or
            TaskCanceledException or JsonException or IOException)
        {
            ResultStatus.Text = "Couldn't load public servers: " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (ServerList.SelectedItem is not PublicServer server)
        {
            MessageBox.Show(this, "Select a server first.", "Viora Manager",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SelectedJobId = server.Id;
        DialogResult = true;
    }

    private void Random_Click(object sender, RoutedEventArgs e)
    {
        if (_servers.Count == 0)
        {
            MessageBox.Show(this, "No matching servers are listed. Adjust the filters and refresh.",
                "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SelectedJobId = _servers[Random.Shared.Next(_servers.Count)].Id;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
