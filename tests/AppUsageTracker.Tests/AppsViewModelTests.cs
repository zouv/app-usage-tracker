using AppUsageTracker.Models;
using AppUsageTracker.Services;
using AppUsageTracker.ViewModels;
using Xunit;

namespace AppUsageTracker.Tests;

public sealed class AppsViewModelTests
{
    private static AppsViewModel CreateViewModel()
    {
        var runtime = new AppRuntime(
            new MemoryAppDataStore(),
            new FakeTimeProvider(new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc)),
            new ApplicationMatcher(),
            new FakeProcessScanner());
        return new AppsViewModel(runtime);
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
}
