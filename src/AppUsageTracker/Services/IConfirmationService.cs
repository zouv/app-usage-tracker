namespace AppUsageTracker.Services;

/// <summary>危险操作确认入口；取消或关闭对话框都视为不确认。</summary>
public interface IConfirmationService
{
    bool Confirm(string message, string title);
}
