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

    /// <summary>倒计时结束动作之一：调用系统锁屏；失败返回 false，由调用方记录降级日志。</summary>
    public static bool TryLockWorkStation() => NativeMethods.LockWorkStation();

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs args)
    {
        if (args.Reason is SessionSwitchReason.SessionLock
            or SessionSwitchReason.ConsoleDisconnect
            or SessionSwitchReason.RemoteDisconnect)
        {
            SetState(SystemSessionState.Locked);
        }
        else if (args.Reason is SessionSwitchReason.SessionUnlock
                 or SessionSwitchReason.SessionLogon
                 or SessionSwitchReason.ConsoleConnect
                 or SessionSwitchReason.RemoteConnect)
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
