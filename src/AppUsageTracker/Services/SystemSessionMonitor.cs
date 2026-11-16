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
            SetState(SystemSessionState.Locked, $"会话切换 {args.Reason}");
        }
        else if (args.Reason is SessionSwitchReason.SessionUnlock
                 or SessionSwitchReason.SessionLogon
                 or SessionSwitchReason.ConsoleConnect
                 or SessionSwitchReason.RemoteConnect)
        {
            SetState(SystemSessionState.Available, $"会话切换 {args.Reason}");
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Suspend)
        {
            SetState(SystemSessionState.Suspended, $"电源模式 {args.Mode}");
        }
        else if (args.Mode == PowerModes.Resume)
        {
            SetState(SystemSessionState.Available, $"电源模式 {args.Mode}");
        }
    }

    /// <summary>状态迁移写 INFO 日志：锁屏/休眠时刻若发生退出，可据此与退出链日志对齐归因。</summary>
    private void SetState(SystemSessionState state, string source)
    {
        if (State == state)
        {
            return;
        }

        AppLogger.Info($"系统状态变化：{source}，{State} → {state}。");
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
