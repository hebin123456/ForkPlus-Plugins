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
	log "RID $RID 的原生件未锁定（status=${STATUS}）→ 跳过取件"
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

# 校验 + 解包 + 选库统一交给 Python（stdlib 的 zipfile / tarfile / hashlib）：
# 不用 unzip / tar / find / sha256sum，避免各平台（尤其 Windows Git-Bash 的 find.exe /
# macOS 无 sha256sum）工具差异；也不需要解出整包，只写出运行时库那 5 个文件。
log "校验 sha256 并解出运行时库到 third_party/ffmpeg/$VERSION/$RID/"
rm -rf "$DEST"
mkdir -p "$DEST"
python3 - "$MANIFEST" "$RID" "$ARCHIVE_FILE" "$DEST" <<'PY' || die "取件失败：sha256 不一致或归档里缺运行时库"
import hashlib
import json
import os
import re
import sys
import tarfile
import zipfile

manifest_path, rid, archive_path, dest = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
data = json.load(open(manifest_path, encoding="utf-8"))
libs = data.get("runtimeLibraries") or ["avformat", "avcodec", "avutil", "swscale", "swresample"]
entry = next((a for a in data.get("artifacts", []) if a.get("rid") == rid), None)
if entry is None:
    sys.exit("manifest 未登记 RID " + rid)

# 1) 校验归档哈希
expected = entry["sha256"]
h = hashlib.sha256()
with open(archive_path, "rb") as f:
    for chunk in iter(lambda: f.read(1 << 20), b""):
        h.update(chunk)
actual = h.hexdigest()
if actual != expected:
    sys.exit("sha256 不一致：期望 %s，实际 %s（上游归档已变，请更新 manifest）" % (expected, actual))
print("  ok: " + actual)

kind = (entry.get("archive") or "tar.xz").lower()
members_dir = entry.get("members") or "lib"
written = []
missing = []


def save(name, reader):
    out = os.path.join(dest, name)
    with open(out, "wb") as f:
        for chunk in iter(lambda: reader.read(1 << 20), b""):
            f.write(chunk)
    written.append(name)


if kind == "zip":
    # Windows：bin/<name>-<major>.dll（归档即此名，原样取）
    with zipfile.ZipFile(archive_path) as zf:
        names = [n for n in zf.namelist() if not n.endswith("/")]
        for lib in libs:
            pat = re.compile(re.escape(lib) + r"-\d+\.dll$")
            match = next(
                (n for n in names
                 if pat.match(n.rsplit("/", 1)[-1])
                 and n.split("/")[-2:-1] == [members_dir]),
                None,
            )
            if match is None:
                missing.append(lib)
                continue
            with zf.open(match) as reader:
                save(match.rsplit("/", 1)[-1], reader)
else:
    # Linux / macOS：取 SONAME（lib<name>.so.<major>，归档里是软链）落成实体文件
    with tarfile.open(archive_path) as tf:
        members = [m for m in tf.getmembers() if m.isfile() or m.issym()]
        for lib in libs:
            pat = re.compile(r"lib" + re.escape(lib) + r"\.so\.\d+$")
            match = next(
                (m for m in members
                 if pat.match(m.name.rsplit("/", 1)[-1])
                 and m.name.split("/")[-2:-1] == [members_dir]),
                None,
            )
            if match is None:
                missing.append(lib)
                continue
            # extractfile() 对软链会跟随到目标内容，正好落成实体文件
            reader = tf.extractfile(match)
            if reader is None:
                missing.append(lib)
                continue
            with reader:
                save(match.name.rsplit("/", 1)[-1], reader)

if missing:
    sys.exit("归档里没找到运行时库：" + " ".join(missing))
print("  wrote: " + " ".join(written))
PY

echo "$SHA256" >"$MARKER"
echo "  已取入以上文件到 ${DEST#$REPO_ROOT/}"
