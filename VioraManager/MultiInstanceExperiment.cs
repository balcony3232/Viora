using System;
using System.Threading;

namespace VioraManager;

/// <summary>
/// Opt-in compatibility experiment against Roblox's legacy singleton event
/// name using a Windows named Mutex. This does not edit the Roblox binary,
/// inject code, touch Roblox cookies, or store account credentials.
///
/// This behaviour is undocumented by Roblox and can stop working at any time.
/// All state lasts only until the user disables it or exits Viora Manager.
/// </summary>
internal sealed class MultiInstanceExperiment : IDisposable
{
    // Roblox also uses an Event named ROBLOX_singletonEvent for its legacy
    // one-Player check. v1.4.7 only held ROBLOX_singletonMutex and thus
    // often failed to affect this check at all.
    //
    // Windows named kernel objects share a namespace. Some older external
    // compatibility tools pre-create a Mutex under the Event's name, so
    // CreateEvent cannot open/create that Event. This is an undocumented
    // compatibility experiment and may not work after Roblox updates.
    private const string LegacySingletonName = "ROBLOX_singletonEvent";

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
                message = "ROBLOX_singletonEvent is already in use. A Roblox session or another instance tool may own it. Close Roblox and other instance tools before trying again.";
                return false;
            }
            _heldMutex = mutex;
            _ownsMutex = true;
            message = "Legacy singleton-event compatibility activated. It may still fail on current Roblox builds. Open one Roblox Player, then test a second account; Viora will check for a second real Player window.";
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
