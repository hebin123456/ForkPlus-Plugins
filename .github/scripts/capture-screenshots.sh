#!/usr/bin/env bash
#
# ForkPlus-Plugins · Pages 截图采集
#
# 目标：在本仓库的 CI 中产出「插件对比视图」的真实截图，供 GitHub Pages 使用。
#
# 全流程（与用户诉求一一对应）：
#   ① 构建仓库内插件（plugins/*/*.csproj）
#   ② 下载最新的 ForkPlus（linux-x64 发行包）
#   ③ 把插件产物（主 DLL + 私有依赖 + 原生库）装入 ForkPlus 的 plugins/ 目录
#   ④ 准备三种 diff 场景（修改 / 新增 / 删除）的 demo 仓库，各自只涉及 sample.pdf
#   ⑤ 无头 X 环境（Xvfb + openbox）启动 ForkPlus，逐场景触发 PDF 插件对比视图，
#      截取完整软件界面（整屏 1920x1280，不做局部裁切）
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

# ── ③ 安装插件产物 ───────────────────────────────────────────────────────────
install_plugins() {
	log "把插件产物（主 DLL + 私有依赖 + 原生库）装入 ForkPlus 的 plugins/ 目录"
	mkdir -p "$APPDIR/plugins"
	shopt -s nullglob
	local count=0
	for proj in "$REPO_ROOT"/plugins/*/*.csproj; do
		local dir
		dir="$(dirname "$proj")"
		bash "$REPO_ROOT/.github/scripts/install-plugin-artifacts.sh" "$dir" "$APPDIR/plugins" "$RID"
		count=$((count + 1))
	done
	[ "$count" -gt 0 ] || die "没有可安装的插件"
	echo "  已安装 $count 个插件"
}

# ── ④ demo 仓库与设置 ────────────────────────────────────────────────────────
# 生成一份最小合法 PDF 作为 .pdf 对比样本（v1 旧 / v2 新）。
# 纯 Python 构造，不依赖 ghostscript 等外部工具；末尾的流对象夹带 NUL 字节，
# 确保 git 把 PDF 判为二进制——只有二进制差异才会走插件路由（见 README）。
write_demo_pdf() {
	local repo="$1" version="$2"
	python3 - "$repo" "$version" <<'PY'
import os, sys
repo, version = sys.argv[1], sys.argv[2]
is_new = version == "v2"

def _escape(text):
    return text.replace("\\", r"\\").replace("(", r"\(").replace(")", r"\)")

def _content(title, lines):
    out = "BT\n/F1 20 Tf\n72 730 Td\n(" + _escape(title) + ") Tj\nET\n"
    y = 690
    for line in lines:
        out += "BT\n/F1 12 Tf\n72 " + str(y) + " Td\n(" + _escape(line) + ") Tj\nET\n"
        y -= 20
    return out.encode("latin-1")

def make_pdf(path, pages):
    objects = {}
    objects[3] = b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
    n = len(pages)
    page_ids = [4 + i for i in range(n)]
    content_ids = [4 + n + i for i in range(n)]
    binary_id = 4 + 2 * n
    kids = " ".join("%d 0 R" % i for i in page_ids)
    objects[1] = b"<< /Type /Catalog /Pages 2 0 R >>"
    objects[2] = ("<< /Type /Pages /Kids [%s] /Count %d >>" % (kids, n)).encode()
    for i, (title, lines) in enumerate(pages):
        objects[page_ids[i]] = (
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
            "/Resources << /Font << /F1 3 0 R >> >> /Contents %d 0 R >>" % content_ids[i]
        ).encode()
        body = _content(title, lines)
        objects[content_ids[i]] = (
            b"<< /Length " + str(len(body)).encode() + b" >>\nstream\n" + body + b"\nendstream"
        )
    binary = bytes([0, 1, 2, 3, 255, 254, 16, 32, 48]) + b"\x00" * 24
    objects[binary_id] = (
        b"<< /Length " + str(len(binary)).encode() + b" >>\nstream\n" + binary + b"\nendstream"
    )
    out = bytearray(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
    offsets = {}
    for num in sorted(objects):
        offsets[num] = len(out)
        out += str(num).encode() + b" 0 obj\n" + objects[num] + b"\nendobj\n"
    xref_off = len(out)
    count = max(objects) + 1
    out += b"xref\n0 " + str(count).encode() + b"\n"
    out += b"0000000000 65535 f \n"
    for num in sorted(objects):
        out += ("%010d 00000 n \n" % offsets[num]).encode()
    out += (
        b"trailer\n<< /Size " + str(count).encode() + b" /Root 1 0 R >>\nstartxref\n"
        + str(xref_off).encode() + b"\n%%EOF\n"
    )
    with open(path, "wb") as f:
        f.write(bytes(out))

title = "ForkPlus Manual v2" if is_new else "ForkPlus Manual v1"
# 文件名排在 sample.example 之后（"sample.example" < "sample.pdf"）：提交视图按路径顺序
# 上下堆叠各文件的差异区，PDF 一页很高，放最后才不会把示例插件的差异区顶出可视范围。
make_pdf(os.path.join(repo, "sample.pdf"), [
    (title, ["NEW revision" if is_new else "old revision", "shared line", "page one body"]),
    ("Chapter 2", ["new content on page two" if is_new else "old content on page two"]),
])
print("  demo PDF ->", os.path.join(repo, "sample.pdf"), "(" + version + ")")
PY
}

# 三种 diff 类型的 demo 仓库：modify（修改）/ add（新增）/ remove（删除）。
# 每个仓库只涉及 sample.pdf 一个文件，命中插件后其对比视图紧贴差异区顶部，
# 不会被别的文件差异区顶下去（PDF 一页很高，尤其需要这一点），截图即可完整呈现。
init_demo_repo() {
	local repo="$1"
	rm -rf "$repo"
	mkdir -p "$repo"
	git -C "$repo" init -q
	git -C "$repo" config user.name "ForkPlus Demo"
	git -C "$repo" config user.email "demo@forkplus.local"
	git -C "$repo" config commit.gpgsign false
}

# sample.pdf 夹带 NUL，确保 git 判为二进制，命中 PDF 插件而非 Hex 兜底。
prepare_repo_pdf() {
	local mode="$1"
	local repo="$WORK/repo-pdf-$mode"
	init_demo_repo "$repo"
	case "$mode" in
	add)
		# 首提交只放无关文本；第二次提交「新增」sample.pdf
		printf 'baseline\n' >"$repo/readme.txt"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: baseline"
		write_demo_pdf "$repo" v2
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "add: sample.pdf"
		;;
	remove)
		# 首提交带 sample.pdf；第二次提交「删除」它
		printf 'baseline\n' >"$repo/readme.txt"
		write_demo_pdf "$repo" v1
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.pdf"
		git -C "$repo" rm -q sample.pdf
		git -C "$repo" commit -q -m "remove: delete sample.pdf"
		;;
	*)
		# modify：两次提交同一文件，老 / 新两侧内容齐全
		write_demo_pdf "$repo" v1
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.pdf"
		write_demo_pdf "$repo" v2
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "update: modify sample.pdf"
		;;
	esac
	echo "  $repo ($mode)"
}

seed_settings() {
	log "预置 ForkPlus 设置（跳过引导 / 亮色主题 / 最大化窗口 / 记录已读更新说明）"
	local dir="$HOME/.local/share/ForkPlus"
	# 每次截图都从干净状态起步：清掉上一次的工作区 / 日志，保证本次只打开目标仓库这一个标签页
	rm -rf "$dir"
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
    "Theme": 0,                                        # 0 = Light（截图用亮色，跟站点风格一致）
    "FollowSystemTheme": False,                        # 关闭跟随系统，否则无头环境会被判成暗色
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
# 打开单个仓库，逐行扫过差异区的文件行；日志一出现目标插件的 marker 就整屏截图。
# marker 由各插件在 SetContent 里写日志给出；日志从头读起，自动加载或点击触发都能命中。
capture_one() {
	local repo="$1" marker="$2" outfile="$3" settle="$4" label="$5"
	log "启动 ForkPlus 打开 $repo（$label）"
	mkdir -p "$OUT"
	( cd "$APPDIR" && DISPLAY="$DISPLAY_NUM" ./ForkPlus "$repo" >"$WORK/forkplus.log" 2>&1 & )

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

	local start_y=$(( SCREEN_H * 45 / 100 ))
	local end_y=$(( SCREEN_H * 99 / 100 ))
	local y hit=0
	# 每点一行就看整份日志是否已含 marker：视图若在启动时已加载，第一轮即命中
	for (( y=start_y; y<=end_y; y+=12 )); do
		DISPLAY="$DISPLAY_NUM" xdotool mousemove "$CLICK_X" "$y" click 1 2>/dev/null || true
		sleep 1.2
		if [ -f "$logfile" ] && grep -qF "$marker" "$logfile"; then
			hit=1
			break
		fi
	done
	[ "$hit" = 1 ] || die "未能触发「$label」对比视图（marker=$marker，见 $logfile）"
	echo "  $label：命中文件行 y=$y"

	sleep "$settle"
	# 截取整个软件界面：整屏（默认 1920x1280），保留菜单 / 工具栏 / 侧栏 / 差异区，不做局部裁切。
	# 文件名与 .github/pages/plugins.json 里登记的截图项一一对应。
	DISPLAY="$DISPLAY_NUM" import -window root "$OUT/$outfile"
	echo "  $label：完整界面截图 -> $OUT/$outfile"

	pkill -x ForkPlus 2>/dev/null || true
	sleep 2
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
prepare_repo_pdf modify
prepare_repo_pdf add
prepare_repo_pdf remove
start_x
# PDF 插件三种场景各截一张：修改 / 新增 / 删除
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-pdf-$mode" "PdfDiffView.SetContent" "pdf-$mode.png" 4 "PDF 插件 · $mode"
done
write_metadata
log "完成：截图已输出到 $OUT"