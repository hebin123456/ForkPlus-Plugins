#!/usr/bin/env bash
#
# ForkPlus-Plugins · Pages 截图采集
#
# 目标：在本仓库的 CI 中产出「插件对比视图」的真实截图，供 GitHub Pages 使用。
#
# 全流程（与用户诉求一一对应）：
#   ① 构建仓库内插件（plugins/*/*.csproj）
#   ② 下载最新的 ForkPlus（linux-x64 发行包）
#   ③ 把插件 DLL 装入 ForkPlus 的 plugins/ 目录
#   ④ 无头 X 环境（Xvfb + openbox）启动 ForkPlus，打开预置 demo 仓库
#   ⑤ 触发示例插件的对比视图并截图（全窗口 + 插件视图区域裁切）
#   ⑥ 产物落到 <repo>/pages/assets/，交由 build-pages.py 生成站点
#
# 仅在 Linux（Xvfb）下工作；CI 使用 ubuntu-latest。本地可用同样命令复现。
#
# 可覆盖环境变量：
#   FP_REPO      ForkPlus 主仓（默认 hebin123456/ForkPlus）
#   RID          目标运行时（默认 linux-x64）
#   WORK         工作目录（默认 /tmp/forkplus-pages）
#   OUT          截图输出目录（默认 <repo>/pages/assets）
#   DISPLAY_NUM  Xvfb 显示号（默认 :77）
#   SCREEN_W/H   虚拟屏尺寸（默认 1920x1280）
#   CLICK_X      demo 仓库文件行的点击横向位置（默认屏宽 33%）
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FP_REPO="${FP_REPO:-hebin123456/ForkPlus}"
RID="${RID:-linux-x64}"
WORK="${WORK:-/tmp/forkplus-pages}"
OUT="${OUT:-$REPO_ROOT/pages/assets}"
DISPLAY_NUM="${DISPLAY_NUM:-:77}"
SCREEN_W="${SCREEN_W:-1920}"
SCREEN_H="${SCREEN_H:-1280}"
CLICK_X="${CLICK_X:-$(( SCREEN_W * 33 / 100 ))}"

APPDIR=""
FORKPLUS_VERSION=""
PLUGIN_VERSION=""

log() { printf '\n\033[1;34m==> %s\033[0m\n' "$*"; }
die() { printf '\n\033[1;31mERROR: %s\033[0m\n' "$*" >&2; exit 1; }

cleanup() {
	pgrep -x ForkPlus >/dev/null 2>&1 && pkill -x ForkPlus || true
	[ -n "${OPENBOX_PID:-}" ] && kill "$OPENBOX_PID" 2>/dev/null || true
	[ -n "${XVFB_PID:-}" ] && kill "$XVFB_PID" 2>/dev/null || true
}
trap cleanup EXIT

# ── ① 构建插件 ───────────────────────────────────────────────────────────────
build_plugins() {
	log "构建仓库内插件 ($RID)"
	shopt -s nullglob
	local projects=("$REPO_ROOT"/plugins/*/*.csproj)
	[ "${#projects[@]}" -gt 0 ] || die "plugins/ 下没有找到插件工程（plugins/*/*.csproj）"
	for proj in "${projects[@]}"; do
		echo "  - $(basename "$(dirname "$proj")")"
		dotnet build "$proj" -c Release -r "$RID" --nologo -v:m
	done
}

# ── ② 下载最新 ForkPlus ──────────────────────────────────────────────────────
download_forkplus() {
	log "下载最新 ForkPlus 发行包（$FP_REPO · linux-x64）"
	# 用 releases/latest 的重定向解析当前 tag（不需要 API / token，避免限流）
	local tag
	tag="$(curl -fsSLI -o /dev/null -w '%{url_effective}' "https://github.com/$FP_REPO/releases/latest" | sed 's:.*/::')"
	[ -n "$tag" ] && [ "$tag" != "latest" ] || die "无法解析最新 Release tag"
	FORKPLUS_VERSION="${tag#v}"
	local asset_url="https://github.com/$FP_REPO/releases/download/$tag/ForkPlus-${FORKPLUS_VERSION}-linux-x64.zip"
	echo "  版本：$FORKPLUS_VERSION"
	echo "  资产：$asset_url"

	mkdir -p "$WORK/app"
	curl -fsSL --retry 3 "$asset_url" -o "$WORK/forkplus.zip"
	unzip -q -o "$WORK/forkplus.zip" -d "$WORK/app"
	APPDIR="$(find "$WORK/app" -maxdepth 2 -type f -name ForkPlus -printf '%h\n' | head -n1)"
	[ -n "$APPDIR" ] || die "解压后未找到 ForkPlus 可执行文件"
	chmod +x "$APPDIR/ForkPlus"
	echo "  安装目录：$APPDIR"
}

# ── ③ 安装插件 DLL ───────────────────────────────────────────────────────────
install_plugins() {
	log "把插件 DLL 装入 ForkPlus 的 plugins/ 目录"
	mkdir -p "$APPDIR/plugins"
	shopt -s nullglob
	local count=0
	for proj in "$REPO_ROOT"/plugins/*/*.csproj; do
		local dir name assembly dll
		dir="$(dirname "$proj")"
		name="$(basename "$dir")"
		assembly="$(sed -n 's:.*<AssemblyName>\([^<]*\)</AssemblyName>.*:\1:p' "$proj" | head -n1)"
		assembly="${assembly:-$name}"
		dll="$dir/bin/Release/net10.0/$RID/$assembly.dll"
		[ -f "$dll" ] || dll="$dir/bin/Release/net10.0/$assembly.dll"
		[ -f "$dll" ] || die "找不到插件产物 $assembly.dll（工程 $dir）"
		cp "$dll" "$APPDIR/plugins/"
		echo "  - $assembly.dll"
		count=$((count + 1))
	done
	[ "$count" -gt 0 ] || die "没有可安装的插件"
	echo "  已安装 $count 个插件"
}

# ── ④ demo 仓库与设置 ────────────────────────────────────────────────────────
prepare_demo_repo() {
	log "准备 demo 仓库（含 .example / .exampletxt 变更）"
	local repo="$WORK/repo"
	rm -rf "$repo"
	mkdir -p "$repo"
	git -C "$repo" init -q
	git -C "$repo" config user.name "ForkPlus Demo"
	git -C "$repo" config user.email "demo@forkplus.local"
	git -C "$repo" config commit.gpgsign false

	# 两次提交：后一次修改前一次的文件。打开仓库时默认选中最新提交，
	# 其文件行即为「修改」对比（old/new 两侧内容齐全），无需再切侧栏视图。
	# 二进制内容（含 NUL）保证 git 判为二进制；扩展名 .example 由示例插件认领
	printf 'ForkPlus plugin demo v1\x00\x01\x02\x03\xff' >"$repo/sample.example"
	printf 'line one\nline two\n' >"$repo/notes.exampletxt"
	git -C "$repo" add -A
	git -C "$repo" commit -q -m "initial: add sample files"

	printf 'ForkPlus plugin demo v2 CHANGED\x00\x10\x20\x30\xfe\xfd' >"$repo/sample.example"
	printf 'line one\nline two\nline three (added)\n' >"$repo/notes.exampletxt"
	git -C "$repo" add -A
	git -C "$repo" commit -q -m "update: modify sample files"
	echo "  仓库：$repo"
}

seed_settings() {
	log "预置 ForkPlus 设置（跳过引导 / 最大化窗口 / 记录已读更新说明）"
	local dir="$HOME/.local/share/ForkPlus"
	mkdir -p "$dir"
	FORKPLUS_VERSION="$FORKPLUS_VERSION" GIT_PATH="$(command -v git || echo /usr/bin/git)" SETTINGS_DIR="$dir" \
		python3 - <<'PY'
import json, os
d = os.environ["SETTINGS_DIR"]
p = os.path.join(d, "settings.json")
cfg = {}
if os.path.exists(p):
    try:
        cfg = json.load(open(p))
    except Exception:
        cfg = {}
cfg.update({
    "Guid": "b3f1c2d4-5e6a-4b7c-8d9e-0f1a2b3c4d5e",   # 非空即跳过首启「用户信息」欢迎窗
    "OnboardingCompleted": True,                       # 跳过新手引导
    "LastShownReleaseNotesVersion": os.environ["FORKPLUS_VERSION"],  # 跳过「更新内容」弹窗
    "GitInstancePath": os.environ["GIT_PATH"],
    "UiLanguage": "zh-Hans",
    "Theme": 0,
    "FollowSystemTheme": True,
    "MainWindowLocationState": {
        "Left": 0.0, "Top": 0.0,
        "Width": 1920.0, "Height": 1280.0,
        "WindowState": 2,   # 2 = Maximized
    },
})
json.dump(cfg, open(p, "w"), indent=2, ensure_ascii=False)
print("  settings.json ->", p)
PY
}

# ── ⑤ 无头 X 环境 ────────────────────────────────────────────────────────────
start_x() {
	log "启动 Xvfb $DISPLAY_NUM（${SCREEN_W}x${SCREEN_H}）+ openbox"
	Xvfb "$DISPLAY_NUM" -screen 0 "${SCREEN_W}x${SCREEN_H}x24" -nolisten tcp >"$WORK/xvfb.log" 2>&1 &
	XVFB_PID=$!
	for _ in $(seq 1 30); do
		DISPLAY="$DISPLAY_NUM" xdpyinfo >/dev/null 2>&1 && break
		sleep 0.5
	done
	DISPLAY="$DISPLAY_NUM" xdpyinfo >/dev/null 2>&1 || die "Xvfb 启动失败"
	DISPLAY="$DISPLAY_NUM" openbox >"$WORK/openbox.log" 2>&1 &
	OPENBOX_PID=$!
	sleep 1
}

# 循环关闭启动期的模态弹窗（git 版本提示 / 更新内容等），直到只剩主窗口
dismiss_dialogs() {
	local tries="${1:-15}"
	local w name
	for _ in $(seq 1 "$tries"); do
		local extra=""
		while read -r w name; do
			case "$name" in
				ForkPlus|N/A|"") continue ;;
			esac
			extra="$w"
			DISPLAY="$DISPLAY_NUM" wmctrl -i -a "$w" 2>/dev/null || true
			DISPLAY="$DISPLAY_NUM" xdotool key --clearmodifiers Return 2>/dev/null || true
			sleep 1
			if DISPLAY="$DISPLAY_NUM" wmctrl -l | grep -q "$w"; then
				# 返回键无效时点右下角「关闭 / 完成」按钮
				local geo x y
				geo="$(DISPLAY="$DISPLAY_NUM" xdotool getwindowgeometry --shell "$w" 2>/dev/null || true)"
				x="$(echo "$geo" | sed -n 's/^X=//p')"
				y="$(echo "$geo" | sed -n 's/^Y=//p')"
				local wd ht
				wd="$(echo "$geo" | sed -n 's/^WIDTH=//p')"
				ht="$(echo "$geo" | sed -n 's/^HEIGHT=//p')"
				[ -n "$x" ] && DISPLAY="$DISPLAY_NUM" xdotool mousemove $((x + wd - 60)) $((y + ht - 28)) click 1 2>/dev/null || true
				sleep 1
			fi
			break
		done < <(DISPLAY="$DISPLAY_NUM" wmctrl -l 2>/dev/null | awk '{id=$1; $1=""; $2=""; $3=""; sub(/^   */,""); print id, $0}')
		[ -n "$extra" ] || return 0
	done
	return 0
}

# ── ⑥ 启动并截图 ─────────────────────────────────────────────────────────────
capture() {
	log "启动 ForkPlus 并打开 demo 仓库"
	mkdir -p "$OUT"
	( cd "$APPDIR" && DISPLAY="$DISPLAY_NUM" ./ForkPlus "$WORK/repo" >"$WORK/forkplus.log" 2>&1 & )

	local ok=0
	# 等待主窗口期间持续关闭启动期模态弹窗（git 版本提示等会阻塞主窗口创建）
	for _ in $(seq 1 90); do
		if DISPLAY="$DISPLAY_NUM" wmctrl -l 2>/dev/null | grep -q 'ForkPlus$'; then ok=1; break; fi
		dismiss_dialogs 1
		sleep 1
	done
	[ "$ok" = 1 ] || die "ForkPlus 主窗口未出现（见 $WORK/forkplus.log）"
	sleep 3
	# 主窗口出现后仍可能有「更新内容」等延迟一帧弹出的模态窗
	dismiss_dialogs 10
	sleep 2

	# 插件加载确认（日志）
	local logfile="$HOME/.local/share/ForkPlus/logs/fork.log"
	grep -q "Diff view plugins loaded" "$logfile" 2>/dev/null \
		&& grep "Diff view plugins loaded" "$logfile" | tail -n1 \
		|| echo "  （未找到插件加载日志，继续）"

	log "定位并点击提交文件行，触发示例插件对比视图"
	local marker="ExampleDiffView.SetContent"
	local before=0
	[ -f "$logfile" ] && before="$(wc -c <"$logfile")"
	local start_y=$(( SCREEN_H * 68 / 100 ))
	local end_y=$(( SCREEN_H * 96 / 100 ))
	local hit_y=""
	local y
	for (( y=start_y; y<=end_y; y+=14 )); do
		DISPLAY="$DISPLAY_NUM" xdotool mousemove "$CLICK_X" "$y" click 1 2>/dev/null || true
		sleep 1.2
		if [ -f "$logfile" ] && tail -c "+$((before + 1))" "$logfile" 2>/dev/null | grep -q "$marker"; then
			hit_y="$y"
			break
		fi
	done
	[ -n "$hit_y" ] || die "未能触发插件对比视图（可调大扫描范围或检查 $logfile）"
	echo "  命中文件行 y=$hit_y"

	sleep 2
	DISPLAY="$DISPLAY_NUM" import -window root "$OUT/example-diff-full.png"
	echo "  全窗口截图 -> $OUT/example-diff-full.png"

	local crop_x=$(( SCREEN_W * 155 / 1000 ))
	local crop_w=$(( SCREEN_W * 84 / 100 ))
	local crop_h=170

	# 插件视图区域裁切：以命中行上方一点为起点，覆盖文件行 + 插件面板
	local crop_y=$(( hit_y - 22 ))
	convert "$OUT/example-diff-full.png" -crop "${crop_w}x${crop_h}+${crop_x}+${crop_y}" +repage \
		"$OUT/example-diff-detail.png"
	echo "  插件视图裁切 -> $OUT/example-diff-detail.png"
}

# ── ⑦ 元数据 ─────────────────────────────────────────────────────────────────
write_metadata() {
	PLUGIN_VERSION="${PLUGIN_VERSION:-$(git -C "$REPO_ROOT" describe --tags --always 2>/dev/null || echo dev)}"
	FORKPLUS_VERSION="$FORKPLUS_VERSION" PLUGIN_VERSION="$PLUGIN_VERSION" OUT="$OUT" \
		python3 - <<'PY'
import json, os, datetime
meta = {
    "forkplus_version": os.environ["FORKPLUS_VERSION"],
    "plugins_version": os.environ["PLUGIN_VERSION"],
    "generated_at": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d %H:%M UTC"),
}
path = os.path.join(os.environ["OUT"], "meta.json")
json.dump(meta, open(path, "w"), indent=2, ensure_ascii=False)
print(path, "->", meta)
PY
}

# ── main ─────────────────────────────────────────────────────────────────────
mkdir -p "$WORK"
build_plugins
download_forkplus
install_plugins
prepare_demo_repo
seed_settings
start_x
capture
write_metadata
log "完成：截图已输出到 $OUT"