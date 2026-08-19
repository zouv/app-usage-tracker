# =============================================================================
# capture-ui.ps1 — 启动应用、切换页面并截图，用于 UI 改动的人工核对
#
# 用法：powershell -File tools/capture-ui.ps1 [-Theme Light|Dark]
# 产物：.runtime/screenshots/*.png
# =============================================================================
[CmdletBinding()]
param(
    [string] $ExePath,
    [string] $OutputDirectory,
    [int]    $SettleSeconds = 6
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $projectRoot 'src/AppUsageTracker/bin/Debug/net8.0-windows/AppUsageTracker.exe'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot '.runtime/screenshots'
}
if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

Add-Type -Namespace Win -Name Api -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
public struct RECT { public int Left, Top, Right, Bottom; }
'@

# 清理上一轮遗留的实例，否则新实例的窗口查找会受干扰
Get-Process -Name 'AppUsageTracker' -ErrorAction SilentlyContinue |
    ForEach-Object { $_.Kill(); $_.WaitForExit(5000) | Out-Null }

$process = Start-Process -FilePath $ExePath -PassThru
Start-Sleep -Seconds $SettleSeconds

try {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)

    # 冷启动耗时不稳定，轮询等待主窗口出现
    $window = $null
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $window = $root.FindFirst(
            [System.Windows.Automation.TreeScope]::Children, $condition)
        if ($null -ne $window) {
            break
        }
        Start-Sleep -Milliseconds 1000
    }

    if ($null -eq $window) {
        throw "未找到主窗口（pid=$($process.Id)）"
    }

    $handle = [IntPtr] $window.Current.NativeWindowHandle

    function Save-Shot {
        param([string] $Name)

        Start-Sleep -Milliseconds 600

        $rect = New-Object Win.Api+RECT
        [Win.Api]::GetWindowRect($handle, [ref] $rect) | Out-Null
        $width = $rect.Right - $rect.Left
        $height = $rect.Bottom - $rect.Top

        # 用 PrintWindow 直接从窗口取图，避免依赖前台状态（截到别的窗口）。
        # 标志位 2 = PW_RENDERFULLCONTENT，WPF 窗口必需。
        $bitmap = New-Object System.Drawing.Bitmap($width, $height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $hdc = $graphics.GetHdc()
        $captured = [Win.Api]::PrintWindow($handle, $hdc, 2)
        $graphics.ReleaseHdc($hdc)
        $graphics.Dispose()

        if (-not $captured) {
            throw "PrintWindow 失败：$Name"
        }

        $path = Join-Path $OutputDirectory "$Name.png"
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
        Write-Output "已截图：$path"
    }

    # 导航项用 Command 绑定驱动页面切换，UIA 的 Select() 只改 IsChecked 不触发 Click，
    # 因此这里用真实鼠标点击元素中心。
    function Invoke-Click {
        param([System.Windows.Automation.AutomationElement] $Element)

        [Win.Api]::SetForegroundWindow($handle) | Out-Null
        Start-Sleep -Milliseconds 400

        $bounds = $Element.Current.BoundingRectangle
        $x = [int] ($bounds.Left + ($bounds.Width / 2))
        $y = [int] ($bounds.Top + ($bounds.Height / 2))
        [Win.Api]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 200
        [Win.Api]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # LEFTDOWN
        [Win.Api]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # LEFTUP
        Start-Sleep -Milliseconds 1200
    }

    function Invoke-Nav {
        param([string] $Text)

        $navCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $Text)
        $item = $window.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants, $navCondition)
        if ($null -eq $item) {
            Write-Warning "未找到导航项：$Text"
            return
        }

        Invoke-Click -Element $item
    }

    function Set-Theme {
        param([string] $Value)

        # 设置页渲染后，主题下拉框是页面内唯一的 ComboBox
        $comboCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ComboBox)

        $combo = $null
        for ($attempt = 0; $attempt -lt 10; $attempt++) {
            $combo = $window.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants, $comboCondition)
            if ($null -ne $combo) {
                break
            }
            Start-Sleep -Milliseconds 500
        }

        if ($null -eq $combo) {
            Write-Warning '未找到主题下拉框'
            return
        }

        Invoke-Click -Element $combo
        Start-Sleep -Milliseconds 600

        $itemCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $Value)
        $item = $combo.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
        if ($null -eq $item) {
            Write-Warning "未找到主题选项：$Value"
            return
        }

        Invoke-Click -Element $item
    }

    Save-Shot -Name '01-overview'
    Invoke-Nav -Text '⚙  设置'
    Save-Shot -Name '02-settings'
    Set-Theme -Value 'Light'
    Save-Shot -Name '03-settings-light'
    Invoke-Nav -Text '▦  概览'
    Save-Shot -Name '04-overview-light'
    Invoke-Nav -Text '◷  时间记录'
    Save-Shot -Name '05-timeline-light'
}
finally {
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit(5000) | Out-Null
    }
}
