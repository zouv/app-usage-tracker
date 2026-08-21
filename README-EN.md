# 时迹 · app-usage-tracker

<div align="center">

[**🇨🇳 中文**](README.md) · **🇬🇧 English**

**Windows Software Usage Tracker**

Tracks only the effective time spent in configured apps that are in the foreground, while the machine is unlocked, awake and in use — so you know exactly where your time goes.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6?style=flat&logo=windows)](#requirements)
[![License](https://img.shields.io/badge/license-TBD-lightgrey?style=flat)](#license)

</div>

## ✨ Features

- **Foreground-aware tracking**: only configured and enabled apps are counted; no time accrues while the machine is locked, asleep, idle, paused, or in privacy mode.
- **Traceable raw sessions**: every start / end / reason is recorded; statistics can be rebuilt from sessions, with backfill, edit, merge and delete support.
- **Multi-dimensional statistics**: daily / weekly / monthly / yearly / all-time, including app rankings, trend bar charts and per-app details.
- **Today overview**: current app, continuous and cumulative durations, a zoomable activity timeline and today's ranking.
- **Tray resident**: runs in the background with a tray menu, auto-start, light / dark themes, and a global hotkey to show / hide.
- **Local-first**: data stays on your machine (JSON), with backup, restore, integrity check and CSV export.

## 🖼️ Screenshots

> The following are early UI mockups; the actual interface may differ from the shipped result.

| Overview | App management |
| --- | --- |
| ![Overview](docs/assets/software-usage-duration-design/01-overview.png) | ![App management](docs/assets/software-usage-duration-design/02-software-management.png) |

| Statistics | Settings |
| --- | --- |
| ![Statistics](docs/assets/software-usage-duration-design/03-statistics.png) | ![Settings](docs/assets/software-usage-duration-design/04-settings.png) |

## 🚀 Quick Start

### Requirements

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git Bash (for running `manager.sh` / `check-docs.sh`)

### Build & Run

```bash
sh manager.sh build   # Build the solution
sh manager.sh start   # Launch the app
```

### Common Commands

| Command | Description |
| --- | --- |
| `sh manager.sh build` | Build the solution |
| `sh manager.sh start` | Launch the WPF app |
| `sh manager.sh test` | Run unit tests |
| `sh manager.sh pack` | Publish a win-x64 self-contained single file to `dist/` |
| `sh manager.sh release <version>` | Bump the version and package a single file (local only; no git/CHANGELOG) |
| `sh manager.sh icon` | Regenerate the app icon (`Assets/app.ico` + `app.png`) |
| `sh manager.sh clean` | Clean build and runtime artifacts |
| `sh manager.sh help` | Show help |

Windows users can skip Git Bash and double-click the `.bat` scripts directly:

| Script | Description |
| --- | --- |
| `build.bat` | Build the solution (args `Debug`/`Release`) |
| `test.bat` | Run unit tests (args `Debug`/`Release`) |
| `publish.bat` | Publish a self-contained single file to `dist/` (args `self`/`fx`) |
| `clean.bat` | Clean `bin`/`obj`/`dist` |
| `ci.bat` | One-shot pipeline: clean → build Release → test Release → publish self |

## 🧱 Tech Stack

| Area | Choice |
| --- | --- |
| Language / runtime | C# / .NET 8 (`net8.0-windows`) |
| Desktop UI | WPF |
| Architecture | MVVM (CommunityToolkit.Mvvm 8.3.2) |
| Foreground monitoring | Win32 P/Invoke (WinEventHook / foreground window) |
| Persistence | System.Text.Json (local JSON, atomic writes) |
| System tray | WinForms NotifyIcon |
| Testing | xUnit |

## 📁 Project Structure

```text
app-usage-tracker/
├── src/AppUsageTracker/          WPF main project (Models/Services/ViewModels/Views/Themes/Controls)
├── tests/AppUsageTracker.Tests/  xUnit unit tests
├── docs/                         Usage, tech, design and roadmap docs
├── tools/                        Icon generation and UI capture scripts
├── manager.sh                    Unified build entrypoint (build/start/test/pack/clean)
├── ARCHITECTURE.md               Code architecture map
├── CHANGELOG.md                  Version milestones
└── HANDOFF.md                    Dev handoff status
```

## 📚 Documentation

> Documentation is currently in Chinese.

- [Usage guide](docs/02-使用说明.md)
- [Tech stack](docs/01-技术栈说明.md)
- [Product design](docs/design/software-usage-duration-design.md)
- [Architecture map](ARCHITECTURE.md)

## ⚖️ License

No open-source license has been specified yet; please confirm before using or distributing.
