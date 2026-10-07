#!/usr/bin/env bash
#
# ForkPlus-Plugins · FFmpeg 原生件获取
#
# 按 RID 把 FFmpeg 的 lgpl-shared 运行时库取到 third_party/ffmpeg/<version>/<rid>/，
# 供插件工程（ForkPlus.Plugins.Audio / Video）在构建时随包拷进输出根。
#
# 二进制**不入库**（见 .gitignore）；版本 / 来源 / 归档名 / sha256 全部锁定在
# third_party/ffmpeg/manifest.json，本脚本据此下载并校验。
#
# 落地的文件名与 FFmpeg.AutoGen 运行期 dlopen / LoadLibrary 的解析规则**逐字对应**：
#   - Linux：`lib<name>.so.<major>`（即 SONAME）。归档里这些是软链，跨平台解包 / 拷贝会
#     丢链，因此这里按链接目标内容落成**实体文件**，且只留这一份（不带 .minor.patch），
#     避免随包分发重复的整份大库。
#   - Windows：`<name>-<major>.dll`（归档即此名，直接取）。
#   - 只取 manifest.runtimeLibraries 列的 5 个库（avformat / avcodec / avutil / swscale /
#     swresample）；avdevice / avfilter / pkgconfig 一概不要（见 design §12）。
#
# 用法：
#   fetch-ffmpeg.sh [rid]        # 默认 $RID，再默认 linux-x64
#
# 环境变量：
#   RID              目标运行时（与用法参数等价）
#   FFMPEG_VERSION   覆盖版本目录（默认取 manifest.version）
#   FFMPEG_FORCE=1   已存在且校验通过也强制重取
#
# 退出码：0 成功（含「已存在、跳过」与「该 RID 未锁定、跳过」）；非 0 失败（下载 / 校验 / 解包）。
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MANIFEST="$REPO_ROOT/third_party/ffmpeg/manifest.json"
RID="${1:-${RID:-linux-x64}}"

log() { printf '\033[1;34m==> %s\033[0m\n' "$*"; }
die() { printf '\033[1;31mERROR: %s\033[0m\n' "$*" >&2; exit 1; }

[ -f "$MANIFEST" ] || die "缺少清单：$MANIFEST"
command -v python3 >/dev/null 2>&1 || die "需要 python3 解析 manifest.json"

# 该 RID 是否已锁定资产（未锁定 → 跳过，插件在该平台降级为「FFmpeg 解码不可用」）
STATUS="$(python3 - "$MANIFEST" "$RID" <<'PY'
import json
import sys

data = json.load(open(sys.argv[1], encoding="utf-8"))
entry = next((a for a in data.get("artifacts", []) if a.get("rid") == sys.argv[2]), None)
print("missing" if entry is None else (entry.get("status") or "unknown"))
PY
)"
case "$STATUS" in
locked) ;;
missing) die "manifest 未登记 RID $RID" ;;
*)
	log "RID $RID 的原生件未锁定（status=$STATUS）→ 跳过取件"
	echo "  该平台不随包分发 FFmpeg，插件降级为「FFmpeg 解码不可用」提示。"
	exit 0
	;;
esac

# 从 manifest 取出本 RID 的锁定信息（tab 分隔：version tag asset sha256 archive members）
read -r VERSION TAG ASSET SHA256 ARCHIVE MEMBERS < <(
	python3 - "$MANIFEST" "$RID" "${FFMPEG_VERSION:-}" <<'PY'
import json
import sys

manifest, rid, override = sys.argv[1], sys.argv[2], sys.argv[3]
data = json.load(open(manifest, encoding="utf-8"))
version = override or data.get("version") or ""
release = data.get("release") or {}
tag = release.get("tag") or "latest"
entry = next((a for a in data.get("artifacts", []) if a.get("rid") == rid), None)
print("\t".join([
    version,
    tag,
    entry["asset"],
    entry["sha256"],
    entry.get("archive", "tar.xz"),
    entry.get("members", "lib"),
]))
PY
) || die "无法解析 manifest.json 中的 RID 信息：$RID"

# 需要随包分发的库（不含 avdevice / avfilter）
RUNTIME_LIBS="$(python3 - "$MANIFEST" <<'PY'
import json
import sys

data = json.load(open(sys.argv[1], encoding="utf-8"))
libs = data.get("runtimeLibraries") or ["avformat", "avcodec", "avutil", "swscale", "swresample"]
print(" ".join(libs))
PY
)"

URL="https://github.com/BtbN/FFmpeg-Builds/releases/download/$TAG/$ASSET"
DEST="$REPO_ROOT/third_party/ffmpeg/$VERSION/$RID"
MARKER="$DEST/.sha256"

log "FFmpeg $VERSION · $RID"
echo "  tag : $TAG"
echo "  url : $URL"

# 已存在且归档哈希一致 → 跳过
if [ -d "$DEST" ] && [ -f "$MARKER" ] && [ "$(cat "$MARKER")" = "$SHA256" ] && [ "${FFMPEG_FORCE:-0}" != "1" ]; then
	echo "  已存在且校验一致，跳过（FFMPEG_FORCE=1 可强制重取）"
	exit 0
fi

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
ARCHIVE_FILE="$TMP/ffmpeg.$ARCHIVE"

log "下载归档"
curl -fsSL --retry 3 -o "$ARCHIVE_FILE" "$URL" || die "下载失败：$URL"

log "校验 sha256"
ACTUAL="$(sha256sum "$ARCHIVE_FILE" | awk '{print $1}')"
if [ "$ACTUAL" != "$SHA256" ]; then
	die "sha256 不一致：期望 $SHA256，实际 $ACTUAL（上游归档已变，请更新 manifest）"
fi
echo "  ok: $ACTUAL"

log "解包到 third_party/ffmpeg/$VERSION/$RID/"
rm -rf "$DEST"
mkdir -p "$DEST"
case "$ARCHIVE" in
tar.xz | tar.gz | tgz) tar -xf "$ARCHIVE_FILE" -C "$TMP" ;;
zip) unzip -q -o "$ARCHIVE_FILE" -d "$TMP/extract" ;;
*) die "未知归档类型：$ARCHIVE" ;;
esac

# 按平台命名规则把 5 个运行时库落成实体文件（见文件头说明）。
count=0
missing=""
if [ "$ARCHIVE" = "zip" ]; then
	# Windows：bin/<name>-<major>.dll
	for lib in $RUNTIME_LIBS; do
		src="$(find "$TMP" -type f -path "*/$MEMBERS/$lib-*.dll" 2>/dev/null | head -n1 || true)"
		if [ -n "$src" ]; then
			cp "$src" "$DEST/"
			count=$((count + 1))
		else
			missing="$missing $lib"
		fi
	done
else
	# Linux / macOS：取 SONAME 链接名（运行期 dlopen 的名字）落成实体文件
	for lib in $RUNTIME_LIBS; do
		src="$(find "$TMP" -type l -regex ".*/$MEMBERS/lib$lib\.so\.[0-9]+" 2>/dev/null | head -n1 || true)"
		if [ -z "$src" ]; then
			# 兜底：没有软链时，按文件名的首个版本号拼出 SONAME
			real="$(find "$TMP" -type f -regex ".*/$MEMBERS/lib$lib\.so\.[0-9]+\(\.[0-9]+\)*" 2>/dev/null | head -n1 || true)"
			if [ -n "$real" ]; then
				major="$(basename "$real" | sed -E "s/^lib.*\.so\.([0-9]+).*/\1/")"
				cp "$real" "$DEST/lib$lib.so.$major"
				count=$((count + 1))
			else
				missing="$missing $lib"
			fi
			continue
		fi
		cp -L "$src" "$DEST/$(basename "$src")"
		count=$((count + 1))
	done
fi

[ -z "$missing" ] || die "归档里没找到运行时库：$missing"
echo "$SHA256" >"$MARKER"
echo "  已取入 $count 个文件："
ls -1 "$DEST" | sed 's/^/    /'
