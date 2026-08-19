using AppUsageTracker.Models;
using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

public class AppColorPaletteTests
{
    [Fact]
    public void AssignMissing_GivesLegacyDefaultAppsDistinctSlots()
    {
        var apps = Enumerable.Range(0, 4)
            .Select(index => new TrackedApp
            {
                Name = $"App{index}",
                ColorHex = AppColorPalette.LegacyDefaultHex,
            })
            .ToList();

        var changed = AppColorPalette.AssignMissing(apps);

        Assert.True(changed);
        Assert.Equal(4, apps.Select(app => app.ColorHex).Distinct().Count());
        Assert.All(apps, app => Assert.Contains(app.ColorHex, AppColorPalette.Slots));
    }

    [Fact]
    public void AssignMissing_KeepsCustomColorsUntouched()
    {
        var custom = new TrackedApp { Name = "自定义", ColorHex = "#123456" };
        var pending = new TrackedApp { Name = "待分配", ColorHex = string.Empty };

        AppColorPalette.AssignMissing([custom, pending]);

        Assert.Equal("#123456", custom.ColorHex);
        Assert.Contains(pending.ColorHex, AppColorPalette.Slots);
    }

    [Fact]
    public void AssignMissing_AvoidsSlotsAlreadyTaken()
    {
        var taken = new TrackedApp { Name = "已占用", ColorHex = AppColorPalette.Slots[0] };
        var pending = new TrackedApp
        {
            Name = "待分配",
            ColorHex = AppColorPalette.LegacyDefaultHex,
        };

        AppColorPalette.AssignMissing([taken, pending]);

        Assert.NotEqual(taken.ColorHex, pending.ColorHex);
    }

    [Fact]
    public void AssignMissing_ReportsNoChangeWhenEverythingIsAssigned()
    {
        var apps = new List<TrackedApp>
        {
            new() { Name = "A", ColorHex = AppColorPalette.Slots[0] },
            new() { Name = "B", ColorHex = AppColorPalette.Slots[1] },
        };

        Assert.False(AppColorPalette.AssignMissing(apps));
    }

    [Fact]
    public void AssignMissing_WrapsAroundWhenAppsOutnumberSlots()
    {
        var count = AppColorPalette.Slots.Count + 3;
        var apps = Enumerable.Range(0, count)
            .Select(index => new TrackedApp { Name = $"App{index}", ColorHex = string.Empty })
            .ToList();

        AppColorPalette.AssignMissing(apps);

        // 超出槽位数后循环复用，但每个槽位的复用次数最多相差一次。
        var groups = apps.GroupBy(app => app.ColorHex).Select(group => group.Count()).ToList();
        Assert.True(groups.Max() - groups.Min() <= 1);
    }

    [Fact]
    public void Resolve_PassesThroughCustomColors()
    {
        Assert.Equal("#123456", AppColorPalette.Resolve("#123456"));
    }

    [Fact]
    public void Resolve_MapsSlotColorsWithinThePalette()
    {
        var resolved = AppColorPalette.Resolve(AppColorPalette.Slots[2]);

        // 未初始化主题时按浅色解析，结果仍是同一个槽位的色值。
        Assert.Equal(AppColorPalette.Slots[2], resolved);
    }
}
