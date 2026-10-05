#!/usr/bin/env bash
#
# ForkPlus-Plugins · 单插件产物安装
#
# 把**一个**插件工程的构建产物装进目标 plugins/ 目录，供 CI 打包与截图流程共用：
#   - 插件自身主 DLL（<AssemblyName>.dll）
#   - 私有托管依赖（如 PDF 插件的 Docnet.Core.dll）
#   - 私有原生库（如 PDF 插件的 pdfium.so / pdfium.dll / pdfium.dylib）
#   - 第三方许可声明（由插件的 third-party.json + licenses/ 全文合并为
#     <AssemblyName>.THIRD-PARTY-NOTICES.txt，见 collect-third-party-notices.py）
#
# 宿主共享程序集（契约 ForkPlus.Plugins.Abstractions / ForkPlus.Plugins.Ui、
# Avalonia、SkiaSharp、HarfBuzzSharp、NLog、MicroCom…）一律**不**随插件分发——
# 运行期由宿主提供同一份程序集，重复分发会造成类型身份漂移（见仓库 README）。
#
# 用法：install-plugin-artifacts.sh <插件工程目录> <目标 plugins 目录> [rid]
set -euo pipefail

projdir="${1:?用法: install-plugin-artifacts.sh <插件工程目录> <目标 plugins 目录> [rid]}"
dest="${2:?缺少目标 plugins 目录}"
rid="${3:-}"

name="$(basename "$projdir")"
assembly="$(sed -n 's:.*<AssemblyName>\([^<]*\)</AssemblyName>.*:\1:p' "$projdir"/*.csproj | head -n1)"
assembly="${assembly:-$name}"

outdir="$projdir/bin/Release/net10.0/${rid}"
if [ -z "$rid" ] || [ ! -d "$outdir" ]; then
	outdir="$projdir/bin/Release/net10.0"
fi
if [ ! -f "$outdir/$assembly.dll" ]; then
	echo "install-plugin-artifacts: 找不到 $assembly.dll（$outdir）" >&2
	exit 1
fi

# 宿主已加载的共享程序集 / 原生库：绝不随插件分发
is_shared() {
	case "$1" in
	ForkPlus.Plugins.Abstractions.dll | ForkPlus.Plugins.Ui.dll | MicroCom.Runtime.dll | NLog.dll | NLog.*.dll | Avalonia.dll | Avalonia.*.dll | SkiaSharp.dll | SkiaSharp.*.dll | libSkiaSharp.* | HarfBuzzSharp.dll | HarfBuzzSharp.*.dll | libHarfBuzzSharp.* | av_libglesv2.* | libAvaloniaNative.*)
		return 0
		;;
	*)
		return 1
		;;
	esac
}

mkdir -p "$dest"
cp "$outdir/$assembly.dll" "$dest/"

# 私有托管依赖 + 私有原生库（linux: *.so / macos: *.dylib / windows: *.dll）
for f in "$outdir"/*.dll "$outdir"/*.so "$outdir"/*.dylib; do
	[ -e "$f" ] || continue
	base="$(basename "$f")"
	[ "$base" = "$assembly.dll" ] && continue
	if is_shared "$base"; then
		echo "  skip shared: $base"
		continue
	fi
	cp "$f" "$dest/"
done

# 第三方许可声明：按插件的 third-party.json 登记表合并 licenses/ 全文。
# 不再依赖 NuGet 恰好落在输出根的 LICENSE（名字不定、多依赖会互相覆盖）。
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
python3 "$script_dir/collect-third-party-notices.py" plugin "$projdir" "$dest/$assembly.THIRD-PARTY-NOTICES.txt"

echo "  installed: $assembly.dll" >&2
echo "$dest/$assembly.dll"