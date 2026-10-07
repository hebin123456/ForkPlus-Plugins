#!/usr/bin/env bash
#
# ForkPlus-Plugins · 三方件交付件获取
#
# 从三方件仓（ForkPlus-Plugins-Third_Party）的最新 Release 取各组件在本 RID 上的交付件，
# 校验 sha256 后解出运行期库到 third_party/<component>/<rid>/，供插件工程按 RID 随包拷进
# 输出根（音视频插件的 FFmpeg 原生库即由此而来）。
#
# **不锁版本**：三方件仓发新 tag 即自动出包，本脚本按 releases/latest 消费，下次构建自动跟上。
# 但**仍然校验 sha256**——哈希取自同一次 Release 的 index.json，既拿到「最新」又不失「防篡改」：
#
#   releases/latest/download/index.json   → 找 component + rid 的 asset / sha256 / libraries
#   releases/latest/download/<asset>      → 下载 → 校验 sha256 → 只解出运行期库
#
# 交付件里另有构建源码的许可全文（LICENSE.md / COPYING.LGPLv2.1）与 component.json，本脚本
# 不解出：插件的许可声明走自己的 licenses/ 与 third-party.json（见 README「三方件」）。
#
# 用法：
#   fetch-third-party.sh [rid]        # 默认 $RID，再默认 linux-x64
#
# 环境变量：
#   RID                  目标运行时（与用法参数等价）
#   THIRD_PARTY_REPO     三方件仓，默认 hebin123456/ForkPlus-Plugins-Third_Party
#   THIRD_PARTY_TAG      指定 tag（默认 latest）；要复现某次构建可钉住，如 THIRD_PARTY_TAG=0.0.1
#   THIRD_PARTY_FORCE=1  已存在且校验通过也强制重取
#
# 退出码：0 成功（含「已存在、跳过」与「该 RID 未收录、跳过」）；非 0 失败（下载 / 校验 / 解包）。
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RID="${1:-${RID:-linux-x64}}"
TP_REPO="${THIRD_PARTY_REPO:-hebin123456/ForkPlus-Plugins-Third_Party}"
TP_TAG="${THIRD_PARTY_TAG:-latest}"
BASE="https://github.com/$TP_REPO/releases/$TP_TAG/download"

log() { printf '\033[1;34m==> %s\033[0m\n' "$*"; }
die() { printf '\033[1;31mERROR: %s\033[0m\n' "$*" >&2; exit 1; }

command -v python3 >/dev/null 2>&1 || die "需要 python3 解析 index.json"

# 组件清单：third_party/<dir>/manifest.json 里登记的 component 字段（缺省用目录名）。
# 一个组件一个目录，与三方件仓一致；新增三方件只需在此加目录，无需改本脚本。
# 不用 mapfile：macOS runner 的 /bin/bash 是 3.2，没有这个内建。
COMPONENTS=()
while IFS= read -r component; do
	[ -n "$component" ] && COMPONENTS+=("$component")
done < <(python3 -c '
import json, os, sys
root = os.path.join(sys.argv[1], "third_party")
for entry in sorted(os.listdir(root)):
    manifest = os.path.join(root, entry, "manifest.json")
    if os.path.isfile(manifest):
        with open(manifest, encoding="utf-8") as f:
            print(json.load(f).get("component") or entry)
' "$REPO_ROOT")
[ "${#COMPONENTS[@]}" -gt 0 ] || die "third_party/ 下没有找到任何组件（<component>/manifest.json）"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

log "取三方件索引：$TP_REPO · $TP_TAG"
curl -fsSL --retry 3 -o "$TMP/index.json" "$BASE/index.json" \
	|| die "下载 index.json 失败：$BASE/index.json（三方件仓还没有 Release？）"

for component in "${COMPONENTS[@]}"; do
	INFO="$(python3 - "$TMP/index.json" "$component" "$RID" <<'PY'
import json
import sys

index_path, component, rid = sys.argv[1], sys.argv[2], sys.argv[3]
with open(index_path, encoding="utf-8") as f:
    index = json.load(f)
for c in index.get("components", []):
    if c.get("component") != component:
        continue
    asset = next((a for a in c.get("assets", []) if a.get("rid") == rid), None)
    if asset is None:
        break
    print("\t".join([
        str(c.get("version") or ""),
        asset["asset"],
        asset["sha256"],
        ",".join(asset.get("libraries") or []),
    ]))
    break
PY
)" || die "解析 index.json 失败（格式与插件仓约定不符）"

	if [ -z "$INFO" ]; then
		log "$component · ${RID}：三方件仓未收录 → 跳过"
		echo "  该平台不随包分发 ${component}，插件降级为「解码不可用」提示。"
		continue
	fi
	IFS=$'\t' read -r VERSION ASSET SHA256 LIBS <<<"$INFO"

	DEST="$REPO_ROOT/third_party/$component/$RID"
	MARKER="$DEST/.source.json"

	log "$component ${VERSION:-?} · $RID"
	echo "  asset : $ASSET"

	# 已存在且哈希一致 → 跳过（省一次几十 MB 的下载）
	if [ -f "$MARKER" ] && [ "${THIRD_PARTY_FORCE:-0}" != "1" ]; then
		CURRENT="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1])).get("sha256",""))' "$MARKER" 2>/dev/null || true)"
		if [ "$CURRENT" = "$SHA256" ]; then
			echo "  已存在且校验一致，跳过（THIRD_PARTY_FORCE=1 可强制重取）"
			continue
		fi
	fi

	curl -fsSL --retry 3 -o "$TMP/$ASSET" "$BASE/$ASSET" || die "下载失败：$BASE/$ASSET"

	# 校验 + 解包交给 Python（stdlib 的 zipfile / hashlib）：不依赖 unzip / sha256sum，
	# Windows 的 Git-Bash 与 macOS 上命名差异都不会踩坑。
	STAGE="$TMP/stage"
	rm -rf "$STAGE"
	mkdir -p "$STAGE"
	python3 - "$TMP/$ASSET" "$SHA256" "$LIBS" "$STAGE" "$component" "$VERSION" "$ASSET" "$TP_TAG" <<'PY' \
		|| die "取件失败：sha256 不一致或交付件里缺运行期库"
import hashlib
import json
import os
import sys
import zipfile

(archive, expected, libs_csv, stage, component, version, asset, tag) = sys.argv[1:9]

digest = hashlib.sha256()
with open(archive, "rb") as f:
    for chunk in iter(lambda: f.read(1 << 20), b""):
        digest.update(chunk)
actual = digest.hexdigest()
if actual != expected:
    sys.exit("sha256 不一致：期望 %s，实际 %s（三方件仓可能刚发了新包，重跑即可）" % (expected, actual))

libs = [x for x in libs_csv.split(",") if x]
with zipfile.ZipFile(archive) as zf:
    # 交付件是平铺的，按 basename 取即可
    members = {os.path.basename(n): n for n in zf.namelist() if not n.endswith("/")}
    missing = [lib for lib in libs if lib not in members]
    if missing:
        sys.exit("交付件里缺运行期库：" + " ".join(missing))
    for lib in libs:
        with zf.open(members[lib]) as src, open(os.path.join(stage, lib), "wb") as out:
            for chunk in iter(lambda: src.read(1 << 20), b""):
                out.write(chunk)

# 记录「这次到底消费了什么」，供排查与人工核对（版本不再写死在插件仓里）
with open(os.path.join(stage, ".source.json"), "w", encoding="utf-8", newline="\n") as f:
    json.dump({
        "component": component,
        "version": version,
        "asset": asset,
        "sha256": actual,
        "tag": tag,
        "libraries": libs,
    }, f, ensure_ascii=False, indent=2)
    f.write("\n")

print("  ok: " + actual[:16] + "…")
print("  wrote: " + " ".join(libs))
PY

	# 校验通过才替换，避免半途失败把上一次可用的件清掉
	rm -rf "$DEST"
	mkdir -p "$(dirname "$DEST")"
	mv "$STAGE" "$DEST"
done

log "完成（${RID}）"
