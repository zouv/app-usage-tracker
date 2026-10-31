using AppUsageTracker.Models;
using AppUsageTracker.Services;
using AppUsageTracker.ViewModels;
using Xunit;

namespace AppUsageTracker.Tests;

public sealed class AppsViewModelTests
{
    private static AppsViewModel CreateViewModel(
        IConfirmationService? confirmationService = null,
        params TrackedApp[] trackedApps)
    {
        var runtime = new AppRuntime(
            new MemoryAppDataStore(),
            new FakeTimeProvider(new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc)),
            new ApplicationMatcher(),
            new FakeProcessScanner());
        runtime.Apps.AddRange(trackedApps);
        return new AppsViewModel(runtime, confirmationService);
    }

    [Fact]
    public void AddNew_OpensEditor()
    {
        var viewModel = CreateViewModel();

        viewModel.AddNewCommand.Execute(null);

        Assert.True(viewModel.IsEditorOpen);
        Assert.Equal("添加软件", viewModel.EditorTitle);
    }

    [Fact]
    public void UseSelectedProcess_FillsEditorFields()
    {
        var viewModel = CreateViewModel();
        viewModel.RunningProcesses.Add(new RunningProcessInfo(
            ProcessId: 42,
            ProcessName: "notepad.exe",
            ExecutablePath: @"C:\Windows\System32\notepad.exe",
            WindowTitle: "文档.txt - 记事本",
            IconBase64: "iVBORw0KGgo="));
        viewModel.SelectedProcess = viewModel.RunningProcesses[0];

        viewModel.UseSelectedProcessCommand.Execute(null);

        Assert.True(viewModel.IsEditorOpen);
        Assert.Equal("记事本", viewModel.EditName);
        Assert.Equal("notepad.exe", viewModel.EditProcessName);
        Assert.Equal(@"C:\Windows\System32\notepad.exe", viewModel.EditExecutablePath);
        Assert.Empty(viewModel.RunningProcesses);
    }

    [Fact]
    public void UseSelectedProcess_WithNoSelection_DoesNotOpenEditor()
    {
        var viewModel = CreateViewModel();

        viewModel.UseSelectedProcessCommand.Execute(null);

        Assert.False(viewModel.IsEditorOpen);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task DeleteSelected_RequiresSingleConfirmation(bool confirmed, int remainingCount)
    {
        var app = new TrackedApp { Name = "Editor", ProcessName = "editor.exe" };
        var confirmation = new FakeConfirmationService(confirmed);
        var viewModel = CreateViewModel(confirmation, app);
        viewModel.SelectedApp = app;

        await viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.Equal(remainingCount, viewModel.Apps.Count);
        Assert.Equal(1, confirmation.CallCount);
        Assert.Contains(app.Name, confirmation.LastMessage);
        if (!confirmed)
        {
            Assert.Same(app, viewModel.SelectedApp);
        }
    }

    [Fact]
    public void CloseProcessScan_ClearsSelection()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProcess = new RunningProcessInfo(42, "app.exe", "C:\\app.exe", "App", string.Empty);

        viewModel.CloseProcessScan();

        Assert.Null(viewModel.SelectedProcess);
        Assert.False(viewModel.UseSelectedProcessCommand.CanExecute(null));
    }
}
