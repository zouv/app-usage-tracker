using Microsoft.Win32;

namespace AppUsageTracker.Services;

public sealed class SystemSessionMonitor : ISystemSessionMonitor
{
    private bool _started;

    public event EventHandler<SystemSessionState>? StateChanged;

    public SystemSessionState State { get; private set; } = SystemSessionState.Available;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    public void Dispose() => Stop();

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs args)
    {
        if (args.Reason == SessionSwitchReason.SessionLock)
        {
            SetState(SystemSessionState.Locked);
        }
        else if (args.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon)
        {
            SetState(SystemSessionState.Available);
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Suspend)
        {
            SetState(SystemSessionState.Suspended);
        }
        else if (args.Mode == PowerModes.Resume)
        {
            SetState(SystemSessionState.Available);
        }
    }

    private void SetState(SystemSessionState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, state);
    }
}
