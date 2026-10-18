using System;
using System.Threading;

namespace VioraManager;

/// <summary>
/// Opt-in compatibility experiment based on a Windows named mutex used by
/// Roblox's legacy singleton check. This does not edit the Roblox binary,
/// inject code, touch Roblox cookies, or store account credentials.
///
/// This behaviour is undocumented by Roblox and can stop working at any time.
/// All state lasts only until the user disables it or exits Viora Manager.
/// </summary>
internal sealed class MultiInstanceExperiment : IDisposable
{
    // The reference project's v2.2 source uses this name. Treat it as an
    // implementation detail that Roblox may change without notice.
    private const string LegacySingletonName = "ROBLOX_singletonMutex";

    private Mutex? _heldMutex;
    private bool _ownsMutex;

    public bool IsEnabled => _heldMutex is not null;

    public bool TryEnable(out string message)
    {
        if (IsEnabled)
        {
            message = "Experimental mode is already enabled.";
            return true;
        }

        try
        {
            var mutex = new Mutex(initiallyOwned: true, name: LegacySingletonName, createdNew: out bool createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                message = "The Roblox singleton name already exists. Another Roblox session or helper may be running. Close Roblox and other multi-instance tools before trying again.";
                return false;
            }
            _heldMutex = mutex;
            _ownsMutex = true;
            message = "Experimental mode enabled for this Manager session. Now launch the first Roblox Player from the official website, then try a different account in a separate browser profile.";
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            message = "Windows denied access to the named mutex. No changes were made.";
            return false;
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Threading.WaitHandleCannotBeOpenedException)
        {
            message = "Unable to enable the compatibility experiment: " + ex.Message;
            return false;
        }
    }

    public void Disable()
    {
        var mutex = _heldMutex;
        _heldMutex = null;
        if (mutex is null) return;
        try
        {
            if (_ownsMutex)
                mutex.ReleaseMutex();
        }
        catch (ApplicationException) { /* Ownership was already lost. */ }
        finally
        {
            _ownsMutex = false;
            mutex.Dispose();
        }
    }

    public void Dispose() => Disable();
}
