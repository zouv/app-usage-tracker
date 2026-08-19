#!/usr/bin/env bash
# =============================================================================
# manager.sh — app-usage-tracker 项目统一入口
# Windows 使用 Git Bash 执行：sh manager.sh <command>
# =============================================================================
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION="${PROJECT_ROOT}/AppUsageTracker.sln"
APP_PROJECT="${PROJECT_ROOT}/src/AppUsageTracker/AppUsageTracker.csproj"
DIST_DIR="${PROJECT_ROOT}/dist"
RUNTIME_DIR="${PROJECT_ROOT}/.runtime"

log()  { printf "\033[1;34m[manager]\033[0m %s\n" "$*"; }
ok()   { printf "\033[1;32m[ ok  ]\033[0m %s\n" "$*"; }
warn() { printf "\033[1;33m[warn ]\033[0m %s\n" "$*"; }
err()  { printf "\033[1;31m[err  ]\033[0m %s\n" "$*" >&2; }

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    err "缺少命令：$1"
    exit 1
  }
}

cmd_build() {
  require_cmd dotnet
  log "构建解决方案"
  dotnet build "${SOLUTION}" "$@"
  ok "构建完成"
}

cmd_start() {
  require_cmd dotnet
  log "启动 WPF 应用"
  dotnet run --project "${APP_PROJECT}" "$@"
}

cmd_test() {
  require_cmd dotnet
  log "运行单元测试"
  dotnet test "${SOLUTION}" "$@"
  ok "测试完成"
}

cmd_pack() {
  require_cmd dotnet
  rm -rf "${DIST_DIR}"
  mkdir -p "${DIST_DIR}"
  log "发布 win-x64 自包含单文件"
  dotnet publish "${APP_PROJECT}" \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishReadyToRun=false \
    -o "${DIST_DIR}" \
    "$@"
  ok "发布完成：${DIST_DIR}"
}

cmd_icon() {
  local script="${PROJECT_ROOT}/tools/generate-icon.ps1"
  local runner=""
  if command -v powershell >/dev/null 2>&1; then
    runner="powershell"
  elif command -v pwsh >/dev/null 2>&1; then
    runner="pwsh"
  else
    err "缺少命令：powershell 或 pwsh（图标生成依赖 System.Drawing）"
    exit 1
  fi

  log "生成应用图标"
  "${runner}" -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "${script}" 2>/dev/null || echo "${script}")"
  ok "图标已生成"
}

cmd_clean() {
  require_cmd dotnet
  dotnet clean "${SOLUTION}" >/dev/null 2>&1 || true
  rm -rf "${DIST_DIR}" "${RUNTIME_DIR}"
  find "${PROJECT_ROOT}" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true
  ok "清理完成"
}

cmd_help() {
  cat <<'EOF'
app-usage-tracker — manager.sh

用法：
  sh manager.sh <command> [args]

命令：
  build [args]   构建解决方案
  start [args]   启动 WPF 应用
  test [args]    运行单元测试
  icon           重新生成应用图标（Assets/app.ico + app.png）
  pack [args]    发布 win-x64 自包含单文件到 dist/
  clean          清理构建、发布和运行时产物
  help           显示帮助
EOF
}

main() {
  local cmd="${1:-help}"
  shift || true
  case "${cmd}" in
    build) cmd_build "$@" ;;
    start|run) cmd_start "$@" ;;
    test) cmd_test "$@" ;;
    icon) cmd_icon ;;
    pack) cmd_pack "$@" ;;
    clean) cmd_clean ;;
    help|-h|--help) cmd_help ;;
    *) err "未知命令：${cmd}"; cmd_help; exit 2 ;;
  esac
}

main "$@"
