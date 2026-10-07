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
#   ④ 准备 demo 仓库：PDF / Office / 压缩包 / 字体 / 可执行文件 / 证书 / 音频 / 视频 各三种 diff 场景
#      （修改 / 新增 / 删除）
#   ⑤ 无头 X 环境（Xvfb + openbox）启动 ForkPlus，逐场景触发对应插件对比视图，
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
	# FFmpeg 原生件：按 third_party/ffmpeg/manifest.json 锁定的 tag / sha256 取件（未锁定的 RID 自动跳过）。
	# 音视频插件按 RID 把原生库随包拷进输出根，故须在构建前完成取件。
	bash "$REPO_ROOT/.github/scripts/fetch-ffmpeg.sh" "$RID"
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

# 生成一份最小合法 OOXML 文档作为 Office 对比样本（.docx/.xlsx/.pptx，v1 旧 / v2 新）。
# 纯标准库（zipfile + 手写 XML）构造，不依赖 python-docx/openpyxl/python-pptx；
# 包内关系与正文均最小但合法，可被 Open XML SDK 正常打开（已在本地用 SDK 校验）。
# Office 文档是 ZIP 包，git 一律判为二进制，因此命中 Office 插件而非文本 / Hex 兜底。
write_demo_office() {
	local repo="$1" version="$2" ext="$3"
	python3 - "$repo" "$version" "$ext" <<'PY'
import os, sys, zipfile
from xml.sax.saxutils import escape as xesc

repo, version, ext = sys.argv[1], sys.argv[2], sys.argv[3]
is_new = version == "v2"

NSPKG = "http://schemas.openxmlformats.org/package/2006/relationships"
CT = "http://schemas.openxmlformats.org/package/2006/content-types"
REL_OFFICE = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
DECL = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'

def e(text):
    return xesc(str(text))

def write_zip(path, files):
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        for name, data in files:
            z.writestr(name, data)
    print("  demo Office ->", path, "(" + version + ")")

def content_types(overrides, defaults=None):
    parts = [DECL, '<Types xmlns="%s">' % CT]
    for ext_name, ct in (defaults or {"rels": "application/vnd.openxmlformats-package.relationships+xml",
                                      "xml": "application/xml"}).items():
        parts.append('<Default Extension="%s" ContentType="%s"/>' % (ext_name, ct))
    for part, ct in overrides.items():
        parts.append('<Override PartName="%s" ContentType="%s"/>' % (part, ct))
    parts.append('</Types>')
    return "".join(parts)

def rels(items):
    parts = [DECL, '<Relationships xmlns="%s">' % NSPKG]
    for rid, rtype, target in items:
        parts.append('<Relationship Id="%s" Type="%s" Target="%s"/>' % (rid, rtype, target))
    parts.append('</Relationships>')
    return "".join(parts)

# ── Word (.docx) ─────────────────────────────────────────────────────────────
def w_para(text):
    return '<w:p><w:r><w:t xml:space="preserve">%s</w:t></w:r></w:p>' % e(text)

def w_heading(text):
    return '<w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>%s</w:t></w:r></w:p>' % e(text)

def w_runs(parts):
    """一段里混排多个 run（text, bold, italic, underline, strike），用于演示内联字符格式。"""
    out = []
    for text, bold, italic, underline, strike in parts:
        rpr = ""
        if bold:
            rpr += "<w:b/>"
        if italic:
            rpr += "<w:i/>"
        if underline:
            rpr += '<w:u w:val="single"/>'
        if strike:
            rpr += "<w:strike/>"
        if rpr:
            rpr = "<w:rPr>" + rpr + "</w:rPr>"
        out.append('<w:r>%s<w:t xml:space="preserve">%s</w:t></w:r>' % (rpr, e(text)))
    return "<w:p>" + "".join(out) + "</w:p>"

def w_table(rows):
    out = ["<w:tbl>"]
    for row in rows:
        out.append("<w:tr>")
        for cell in row:
            out.append("<w:tc>" + w_para(cell) + "</w:tc>")
        out.append("</w:tr>")
    out.append("</w:tbl>")
    return "".join(out)

def build_docx():
    blocks = [
        w_heading("ForkPlus Manual"),
        # 版本行：值加粗——演示 Word run 的粗体投影。
        w_runs([("Revision: ", False, False, False, False), (version, True, False, False, False)]),
        # 一行覆盖四种内联格式，截图里能直接看到加粗 / 斜体 / 下划线 / 删除线。
        w_runs([
            ("Bold", True, False, False, False),
            (" · ", False, False, False, False),
            ("Italic", False, True, False, False),
            (" · ", False, False, False, False),
            ("Underline", False, False, True, False),
            (" · ", False, False, False, False),
            ("Strike", False, False, False, True),
        ]),
        w_para("This paragraph is identical on both sides."),
        w_para("New: export to SVG." if is_new else "Old: export to PNG only."),
        w_table([["Component", "Status"], ["Compare", "GA"], ["Sync", "GA" if is_new else "beta"]]),
    ]
    document = (DECL + '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'
                "<w:body>" + "".join(blocks) + "</w:body></w:document>")
    write_zip(os.path.join(repo, "sample.docx"), [
        ("[Content_Types].xml", content_types({
            "/word/document.xml":
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"})),
        ("_rels/.rels", rels([("rId1", REL_OFFICE, "word/document.xml")])),
        ("word/document.xml", document),
    ])

# ── Excel (.xlsx) ────────────────────────────────────────────────────────────
def x_cell(ref, value):
    if isinstance(value, (int, float)):
        return '<c r="%s"><v>%s</v></c>' % (ref, value)
    return '<c r="%s" t="inlineStr"><is><t xml:space="preserve">%s</t></is></c>' % (ref, e(value))

def x_row(index, values):
    cells = "".join(x_cell(chr(ord("A") + i) + str(index), v) for i, v in enumerate(values))
    return '<row r="%d">%s</row>' % (index, cells)

def build_xlsx():
    rows = [
        ["Item", "Q1", "Q2"],
        ["Licenses", 1200, 1500],
        ["Hardware", 800, 1200 if is_new else 900],
        ["Support", "included", "included" if is_new else "trial"],
    ]
    sheet = (DECL + '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
             "<sheetData>" + "".join(x_row(i + 1, r) for i, r in enumerate(rows)) + "</sheetData></worksheet>")
    workbook = (DECL + '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
                'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
                '<sheets><sheet name="Budget" sheetId="1" r:id="rId1"/></sheets></workbook>')
    write_zip(os.path.join(repo, "sample.xlsx"), [
        ("[Content_Types].xml", content_types({
            "/xl/workbook.xml":
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
            "/xl/worksheets/sheet1.xml":
                "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"})),
        ("_rels/.rels", rels([("rId1", REL_OFFICE, "xl/workbook.xml")])),
        ("xl/workbook.xml", workbook),
        ("xl/_rels/workbook.xml.rels", rels([
            ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
             "worksheets/sheet1.xml")])),
        ("xl/worksheets/sheet1.xml", sheet),
    ])

# ── PowerPoint (.pptx) ───────────────────────────────────────────────────────
def p_slide(lines):
    shapes = []
    y = 838200
    for i, line in enumerate(lines):
        sid = i + 2
        # 每张幻灯片的首行加粗（标题行），演示 PowerPoint run 的粗体投影。
        bold = ' b="1"' if i == 0 else ""
        body = ('<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang="en-US" dirty="0"%s/>'
                "<a:t>%s</a:t></a:r></a:p></p:txBody>" % (bold, e(line)))
        shapes.append(
            '<p:sp><p:nvSpPr><p:cNvPr id="%d" name="TextBox %d"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr>'
            '<p:spPr><a:xfrm><a:off x="838200" y="%d"/><a:ext cx="8229600" cy="457200"/></a:xfrm>'
            '<a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr>%s</p:sp>' % (sid, sid, y, body))
        y += 457200
    return (DECL + '<p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" '
            'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" '
            'xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">'
            "<p:cSld><p:spTree>"
            '<p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>'
            + "".join(shapes) + "</p:spTree></p:cSld></p:sld>")

def build_pptx():
    slides = [
        ["ForkPlus", "Release %s" % version],
        ["Roadmap", "SVG export" if is_new else "PNG export", "PDF compare"],
    ]
    slide_ct = "application/vnd.openxmlformats-officedocument.presentationml.slide+xml"
    presentation = (DECL + '<p:presentation xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" '
                    'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" '
                    'xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">'
                    '<p:sldIdLst><p:sldId id="256" r:id="rId1"/><p:sldId id="257" r:id="rId2"/></p:sldIdLst>'
                    '<p:sldSz cx="9144000" cy="6858000" type="screen4x3"/><p:notesSz cx="6858000" cy="9144000"/>'
                    "</p:presentation>")
    files = [
        ("[Content_Types].xml", content_types({
            "/ppt/presentation.xml":
                "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml",
            "/ppt/slides/slide1.xml": slide_ct,
            "/ppt/slides/slide2.xml": slide_ct})),
        ("_rels/.rels", rels([("rId1", REL_OFFICE, "ppt/presentation.xml")])),
        ("ppt/presentation.xml", presentation),
        ("ppt/_rels/presentation.xml.rels", rels([
            ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide",
             "slides/slide1.xml"),
            ("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide",
             "slides/slide2.xml")])),
    ]
    for i, lines in enumerate(slides):
        files.append(("ppt/slides/slide%d.xml" % (i + 1), p_slide(lines)))
    write_zip(os.path.join(repo, "sample.pptx"), files)

{"docx": build_docx, "xlsx": build_xlsx, "pptx": build_pptx}[ext]()
PY
}

# 生成一份压缩包对比样本（.zip / .tar.gz / .tar.xz，v1 旧 / v2 新）。
# 纯标准库（zipfile + tarfile）构造，不依赖 7z 等外部工具：包内是一个两级目录树
# （docs/ + assets/ + 顶层 README.md），v2 比 v1 多一个 docs/release-notes.md 并改动
# changelog，正好体现「压缩包展开成条目树后左右对比」。tar 用 GNU 格式，避免 PAX 扩展头
# 在条目树里多出 @PaxHeader 噪音行。压缩包一律为二进制，git 判为二进制，命中 Archive 插件。
write_demo_archive() {
	local repo="$1" version="$2" ext="$3"
	python3 - "$repo" "$version" "$ext" <<'PY'
import io, os, sys, tarfile, zipfile
repo, version, ext = sys.argv[1], sys.argv[2], sys.argv[3]
is_new = version == "v2"

def enc(text):
    return text.encode("utf-8")

files = [
    ("docs/guide.md", enc("# ForkPlus Guide\nRevision: %s\n" % version)),
    ("docs/changelog.md", enc("## %s\n- %s\n" % (version, "new: SVG export" if is_new else "old: PNG export"))),
    ("assets/logo.bin", bytes(range(16))),
    ("README.md", enc("ForkPlus sample archive (%s)\n" % version)),
]
if is_new:
    files.append(("docs/release-notes.md", enc("Release notes for v2\n")))

name = "sample." + ext
target = os.path.join(repo, name)
if ext == "zip":
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as z:
        for n, data in files:
            z.writestr(n, data)
elif ext in ("tar.gz", "tgz", "tar.xz", "tar.bz2"):
    mode = {"tar.gz": "w:gz", "tgz": "w:gz", "tar.xz": "w:xz", "tar.bz2": "w:bz2"}[ext]
    with tarfile.open(target, mode, format=tarfile.GNU_FORMAT) as t:
        for n, data in files:
            info = tarfile.TarInfo(n)
            info.size = len(data)
            t.addfile(info, io.BytesIO(data))
else:
    raise SystemExit("unsupported demo archive ext: " + ext)
print("  demo archive ->", target, "(" + version + ")")
PY
}

# ── demo 素材：字体 ──────────────────────────────────────────────────────────
# 字体插件靠「同一句话的多字号样张 + sfnt 表结构 diff（name / head / OS/2 / maxp / hhea /
# cmap）」呈现差异。字体文件无法用标准库从零构造，也不便在没有 fontTools 的环境里原地
# 改写，因此直接取 CI runner 上 apt 安装的两套系统字体充当旧 / 新两侧：DejaVu Sans（旧）
# 与 DejaVu Serif（新）。两者家族 / 版本 / 字重 / 字形 / 码位覆盖都有真实差异——样张并排
# 即可看出字形变化，元数据与码位模式也能看到逐行标注。
FONT_OLD_CANDIDATES=(
	/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf
	/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf
)
FONT_NEW_CANDIDATES=(
	/usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf
	/usr/share/fonts/truetype/liberation/LiberationSerif-Regular.ttf
	/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf
)

# 取第一个存在的候选路径；都不存在则返回非零（pages.yml 已 apt 安装这些字体包）。
first_existing() {
	local path
	for path in "$@"; do
		if [ -f "$path" ]; then
			printf '%s\n' "$path"
			return 0
		fi
	done
	return 1
}

write_demo_font() {
	local repo="$1" version="$2"
	local src
	if [ "$version" = "v2" ]; then
		src="$(first_existing "${FONT_NEW_CANDIDATES[@]}")" || die "找不到可用系统字体（请 apt 安装 fonts-dejavu / fonts-liberation）"
	else
		src="$(first_existing "${FONT_OLD_CANDIDATES[@]}")" || die "找不到可用系统字体（请 apt 安装 fonts-dejavu / fonts-liberation）"
	fi
	# 统一叫 sample.ttf：两侧同名才能构成「同一文件旧 / 新两版」的修改场景。
	cp "$src" "$repo/sample.ttf"
	echo "  demo font -> $repo/sample.ttf ($version, 源: $(basename "$src"))"
}

# ── demo 素材：可执行文件 / 库 ──────────────────────────────────────────────
# 三种场景各用一种真实格式（都用 CI runner 自带工具现造），一次覆盖 ELF / PE / ar 三条
# 解析路径，也覆盖「结构摘要 / 段节表 / 导入导出 / 体积构成」四种模式所需的字段：
#   modify → .so（ELF 共享库，cc 编译；v2 多一个导出符号）
#   add    → .dll（.NET 托管 PE，dotnet build 出最小类库）
#   remove → .a（ar 归档，ar rcs 打包两个目标文件）

# 用 cc 编一个最小 ELF 共享库；v2 额外导出一个符号，制造「导出符号」差异。
write_demo_elf() {
	local out="$1" version="$2" tmp="$3"
	local rev="$version"
	cat >"$tmp/sample.c" <<EOF
#include <stdio.h>
__attribute__((visibility("default"))) const char *forkplus_revision(void) { return "$rev"; }
__attribute__((visibility("default"))) int sample_add(int a, int b) { return a + b; }
__attribute__((visibility("default"))) void sample_log(void) { puts("forkplus sample library"); }
EOF
	if [ "$version" = "v2" ]; then
		cat >>"$tmp/sample.c" <<'EOF'
__attribute__((visibility("default"))) int sample_mul(int a, int b) { return a * b; }
EOF
	fi
	# -Wl,-soname 让 .dynamic 带上 SONAME；puts 让动态段出现 NEEDED libc。
	cc -shared -fPIC -O2 -Wl,-soname,libsample.so -o "$out" "$tmp/sample.c"
}

# 用 dotnet 编一个最小托管类库，产出的 sample.dll 是真实 PE（含 COR 元数据）。
write_demo_pe() {
	local out="$1" version="$2" tmp="$3"
	local proj="$tmp/peproj"
	mkdir -p "$proj"
	cat >"$proj/sample.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>sample</AssemblyName>
    <RootNamespace>ForkPlus.Sample</RootNamespace>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
EOF
	cat >"$proj/Sample.cs" <<EOF
namespace ForkPlus.Sample
{
	public static class Sample
	{
		public const string Revision = "$version";
		public static int Add(int a, int b) { return a + b; }
	}
}
EOF
	# 需要 dotnet 在 PATH（pages.yml 已 setup-dotnet）；-v:q 压缩日志噪音。
	dotnet build "$proj/sample.csproj" -c Release -o "$tmp/peout" --nologo -v:q
	cp "$tmp/peout/sample.dll" "$out"
}

# 用 ar 打一个静态库（.a）：两个目标文件 → 归档里两个成员 + 符号表。
write_demo_ar() {
	local out="$1" tmp="$2"
	printf 'int sample_one(void) { return 1; }\n' >"$tmp/one.c"
	printf 'int sample_two(void) { return 2; }\n' >"$tmp/two.c"
	cc -c -o "$tmp/one.o" "$tmp/one.c"
	cc -c -o "$tmp/two.o" "$tmp/two.c"
	rm -f "$out"
	ar rcs "$out" "$tmp/one.o" "$tmp/two.o"
}

# ── demo 素材：证书 ──────────────────────────────────────────────────────────
# 三种场景各用一种容器（都用 CI runner 自带的 openssl 现造）：
#   modify → .der（自签 DER 证书，v2 改 SAN / 有效期 / 密钥长度）
#   add    → .p12（PKCS#12 证书链，空密码 → 视图无需输入密码即可读出证书）
#   remove → .p7b（PKCS#7 证书袋）
# 证书带 SAN / KeyUsage / EKU / BasicConstraints / CRL / OCSP 等扩展，让「用途 / 扩展」
# 分组与有效期时间轴、SAN 彩色徽章这些可视化元素都能呈现出来。
write_demo_certificate() {
	local repo="$1" version="$2" fmt="$3"
	local tmp="$WORK/demo-certificate-$fmt"
	rm -rf "$tmp"
	mkdir -p "$tmp"
	local days bits subject san
	if [ "$version" = "v2" ]; then
		days=825
		bits=4096
		subject="/C=US/ST=CA/L=San Francisco/O=ForkPlus/OU=Release/CN=sample.example"
		san="DNS:sample.example,DNS:api.example,DNS:www.example,IP:10.0.0.2,email:dev@example.com"
	else
		days=365
		bits=2048
		subject="/C=US/ST=CA/L=San Francisco/O=ForkPlus/OU=Release/CN=sample.example"
		san="DNS:sample.example,IP:10.0.0.1"
	fi
	openssl req -x509 -newkey "rsa:$bits" -nodes -sha256 -days "$days" \
		-keyout "$tmp/key.pem" -out "$tmp/cert.pem" \
		-subj "$subject" \
		-addext "subjectAltName=$san" \
		-addext "keyUsage=critical,digitalSignature,keyEncipherment" \
		-addext "extendedKeyUsage=serverAuth,clientAuth" \
		-addext "basicConstraints=critical,CA:FALSE" \
		-addext "crlDistributionPoints=URI:http://crl.example.com/sample.crl" \
		-addext "authorityInfoAccess=OCSP;URI:http://ocsp.example.com" \
		>/dev/null 2>&1
	case "$fmt" in
	der)
		openssl x509 -in "$tmp/cert.pem" -outform DER -out "$repo/sample.der"
		;;
	p12)
		# -passout pass: → 空密码；插件用空密码即可读出证书链（安全边界：只读证书、不碰私钥）。
		openssl pkcs12 -export -inkey "$tmp/key.pem" -in "$tmp/cert.pem" \
			-name "ForkPlus Sample" -out "$repo/sample.p12" -passout pass:
		;;
	p7b)
		openssl crl2pkcs7 -nocrl -certfile "$tmp/cert.pem" | openssl pkcs7 -outform DER -out "$repo/sample.p7b"
		;;
	esac
	rm -rf "$tmp"
	echo "  demo certificate -> $repo/sample.$fmt ($version)"
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

# Office demo 仓库：三种 diff 场景各用一种现代 OOXML 格式（扩大覆盖面，三张截图正好演示
# Word / Excel / PowerPoint 三件套）——modify 用 .docx（段落 + 标题 + 表格）、add 用 .xlsx
# （工作表网格）、remove 用 .pptx（幻灯片文字）。Office 文档是 ZIP 包，git 判为二进制，
# 命中 Office 插件而非文本 / Hex 兜底。
prepare_repo_office() {
	local mode="$1" ext="$2"
	local repo="$WORK/repo-office-$mode"
	init_demo_repo "$repo"
	case "$mode" in
	add)
		printf 'baseline\n' >"$repo/readme.txt"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: baseline"
		write_demo_office "$repo" v2 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "add: sample.$ext"
		;;
	remove)
		printf 'baseline\n' >"$repo/readme.txt"
		write_demo_office "$repo" v1 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.$ext"
		git -C "$repo" rm -q "sample.$ext"
		git -C "$repo" commit -q -m "remove: delete sample.$ext"
		;;
	*)
		write_demo_office "$repo" v1 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.$ext"
		write_demo_office "$repo" v2 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "update: modify sample.$ext"
		;;
	esac
	echo "  $repo ($mode · .$ext)"
}

# 压缩包 demo 仓库：三种 diff 场景各用一种包格式——modify 用 .zip（显式目录树）、
# add 用 .tar.gz（gzip + tar 组合）、remove 用 .tar.xz。压缩包为二进制，git 判为二进制，
# 命中 Archive 插件而非文本 / Hex 兜底。
prepare_repo_archive() {
	local mode="$1" ext="$2"
	local repo="$WORK/repo-archive-$mode"
	init_demo_repo "$repo"
	case "$mode" in
	add)
		printf 'baseline\n' >"$repo/readme.txt"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: baseline"
		write_demo_archive "$repo" v2 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "add: sample.$ext"
		;;
	remove)
		printf 'baseline\n' >"$repo/readme.txt"
		write_demo_archive "$repo" v1 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.$ext"
		git -C "$repo" rm -q "sample.$ext"
		git -C "$repo" commit -q -m "remove: delete sample.$ext"
		;;
	*)
		write_demo_archive "$repo" v1 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.$ext"
		write_demo_archive "$repo" v2 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "update: modify sample.$ext"
		;;
	esac
	echo "  $repo ($mode · .$ext)"
}

# 字体 demo 仓库：三场景都用同一对系统字体（旧 DejaVu Sans / 新 DejaVu Serif），
# 两侧文件名同为 sample.ttf，构成「同一文件旧 / 新两版」的修改场景；字体含 NUL 字节，
# git 判为二进制，命中字体插件而非 Hex 兜底。样张 / 元数据 / 码位三模式都有真实差异。
prepare_repo_font() {
	local mode="$1"
	local repo="$WORK/repo-font-$mode"
	init_demo_repo "$repo"
	case "$mode" in
	add)
		printf 'baseline\n' >"$repo/readme.txt"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: baseline"
		write_demo_font "$repo" v2
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "add: sample.ttf"
		;;
	remove)
		printf 'baseline\n' >"$repo/readme.txt"
		write_demo_font "$repo" v1
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.ttf"
		git -C "$repo" rm -q sample.ttf
		git -C "$repo" commit -q -m "remove: delete sample.ttf"
		;;
	*)
		write_demo_font "$repo" v1
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.ttf"
		write_demo_font "$repo" v2
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "update: modify sample.ttf"
		;;
	esac
	echo "  $repo ($mode)"
}

# 可执行文件 demo 仓库：modify 用 .so（ELF 共享库，v2 多一个导出符号）、add 用 .dll
# （.NET 托管 PE）、remove 用 .a（ar 归档）——一次覆盖四条解析路径与四种模式所需字段。
prepare_repo_executable() {
	local mode="$1" ext="$2"
	local repo="$WORK/repo-executable-$mode"
	local tmp="$WORK/demo-executable-$mode"
	init_demo_repo "$repo"
	rm -rf "$tmp"
	mkdir -p "$tmp"
	case "$mode" in
	add)
		printf 'baseline\n' >"$repo/readme.txt"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: baseline"
		write_demo_pe "$repo/sample.$ext" v2 "$tmp"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "add: sample.$ext"
		;;
	remove)
		printf 'baseline\n' >"$repo/readme.txt"
		write_demo_ar "$repo/sample.$ext" "$tmp"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.$ext"
		git -C "$repo" rm -q "sample.$ext"
		git -C "$repo" commit -q -m "remove: delete sample.$ext"
		;;
	*)
		write_demo_elf "$repo/sample.$ext" v1 "$tmp"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.$ext"
		write_demo_elf "$repo/sample.$ext" v2 "$tmp"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "update: modify sample.$ext"
		;;
	esac
	rm -rf "$tmp"
	echo "  $repo ($mode · .$ext)"
}

# 证书 demo 仓库：modify 用 .der（自签 DER 证书）、add 用 .p12（PKCS#12）、remove 用 .p7b
# （PKCS#7 证书袋）。v2 改 SAN / 有效期 / 密钥长度，让详情行与有效期时间轴、SAN 徽章都有差异。
prepare_repo_certificate() {
	local mode="$1" fmt="$2"
	local repo="$WORK/repo-certificate-$mode"
	init_demo_repo "$repo"
	case "$mode" in
	add)
		printf 'baseline\n' >"$repo/readme.txt"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: baseline"
		write_demo_certificate "$repo" v2 "$fmt"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "add: sample.$fmt"
		;;
	remove)
		printf 'baseline\n' >"$repo/readme.txt"
		write_demo_certificate "$repo" v1 "$fmt"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.$fmt"
		git -C "$repo" rm -q "sample.$fmt"
		git -C "$repo" commit -q -m "remove: delete sample.$fmt"
		;;
	*)
		write_demo_certificate "$repo" v1 "$fmt"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.$fmt"
		write_demo_certificate "$repo" v2 "$fmt"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "update: modify sample.$fmt"
		;;
	esac
	echo "  $repo ($mode · .$fmt)"
}

# ── demo 素材：音频 ──────────────────────────────────────────────────────────
# 音频样本用 CI runner 上的系统 ffmpeg CLI 现造（pages.yml 已 apt 安装 ffmpeg）。
# 注意分工：造样本用系统 ffmpeg，插件解码用随包分发的 FFmpeg 原生库，两者互不相干。
#
# 三种场景各用一种容器，覆盖有损 / 无损两条解码路径：
#   modify → .mp3（libmp3lame 有损）
#   add    → .wav（PCM 无损）
#   remove → .flac（FLAC 无损压缩）
# v2 比 v1 换了频率（440→660 Hz）、时长（3→4 s）、采样率（44100→48000）并改了标签，
# 让元数据表（时长 / 码率 / 采样率 / 标签）与波形 / 频谱都有真实差异。
write_demo_audio() {
	local repo="$1" version="$2" ext="$3"
	local freq dur rate codec
	if [ "$version" = "v2" ]; then
		freq=660
		dur=4
		rate=48000
	else
		freq=440
		dur=3
		rate=44100
	fi
	case "$ext" in
	wav) codec=pcm_s16le ;;
	flac) codec=flac ;;
	*) codec=libmp3lame ;;
	esac
	ffmpeg -hide_banner -loglevel error \
		-f lavfi -i "sine=frequency=$freq:duration=$dur" \
		-ac 2 -ar "$rate" \
		-metadata title="ForkPlus Demo $version" -metadata artist="ForkPlus" \
		-metadata album="Audio Demo" -metadata date="2026" \
		-codec:a "$codec" -y "$repo/sample.$ext"
	echo "  demo audio -> $repo/sample.$ext ($version)"
}

# 音频 demo 仓库：三场景各用一种容器（modify=mp3 / add=wav / remove=flac）。
# 音频文件含 NUL 字节，git 判为二进制，命中音频插件而非文本 / Hex 兜底。
prepare_repo_audio() {
	local mode="$1" ext="$2"
	local repo="$WORK/repo-audio-$mode"
	init_demo_repo "$repo"
	case "$mode" in
	add)
		printf 'baseline\n' >"$repo/readme.txt"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: baseline"
		write_demo_audio "$repo" v2 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "add: sample.$ext"
		;;
	remove)
		printf 'baseline\n' >"$repo/readme.txt"
		write_demo_audio "$repo" v1 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.$ext"
		git -C "$repo" rm -q "sample.$ext"
		git -C "$repo" commit -q -m "remove: delete sample.$ext"
		;;
	*)
		write_demo_audio "$repo" v1 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.$ext"
		write_demo_audio "$repo" v2 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "update: modify sample.$ext"
		;;
	esac
	echo "  $repo ($mode · .$ext)"
}

# ── demo 素材：视频 ──────────────────────────────────────────────────────────
# 视频三种场景各用一种容器 / 编码：
#   modify → .mp4（H.264）
#   add    → .mkv（H.264）
#   remove → .avi（MPEG-4 Part 2）
# v2 换成 testsrc2 并整体色相旋转 90°，让帧条与单帧对比里的画面明显不同。
write_demo_video() {
	local repo="$1" version="$2" ext="$3"
	local src codec
	if [ "$version" = "v2" ]; then
		src="testsrc2=size=320x240:rate=10:duration=3,hue=h=90"
	else
		src="testsrc=size=320x240:rate=10:duration=2"
	fi
	case "$ext" in
	avi) codec=mpeg4 ;;
	*) codec=libx264 ;;
	esac
	ffmpeg -hide_banner -loglevel error \
		-f lavfi -i "$src" \
		-pix_fmt yuv420p -c:v "$codec" \
		-metadata title="ForkPlus Demo $version" \
		-y "$repo/sample.$ext"
	echo "  demo video -> $repo/sample.$ext ($version)"
}

# 视频 demo 仓库：三场景各用一种容器（modify=mp4 / add=mkv / remove=avi）。
prepare_repo_video() {
	local mode="$1" ext="$2"
	local repo="$WORK/repo-video-$mode"
	init_demo_repo "$repo"
	case "$mode" in
	add)
		printf 'baseline\n' >"$repo/readme.txt"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: baseline"
		write_demo_video "$repo" v2 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "add: sample.$ext"
		;;
	remove)
		printf 'baseline\n' >"$repo/readme.txt"
		write_demo_video "$repo" v1 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.$ext"
		git -C "$repo" rm -q "sample.$ext"
		git -C "$repo" commit -q -m "remove: delete sample.$ext"
		;;
	*)
		write_demo_video "$repo" v1 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.$ext"
		write_demo_video "$repo" v2 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "update: modify sample.$ext"
		;;
	esac
	echo "  $repo ($mode · .$ext)"
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
# Office demo：三场景各用一种格式（modify=docx / add=xlsx / remove=pptx）
prepare_repo_office modify docx
prepare_repo_office add xlsx
prepare_repo_office remove pptx
# 压缩包 demo：三场景各用一种包格式（modify=zip / add=tar.gz / remove=tar.xz）
prepare_repo_archive modify zip
prepare_repo_archive add tar.gz
prepare_repo_archive remove tar.xz
# 字体 demo：三场景均用同一对系统字体（旧 DejaVu Sans / 新 DejaVu Serif）
prepare_repo_font modify
prepare_repo_font add
prepare_repo_font remove
# 可执行文件 demo：三场景各用一种格式（modify=so(ELF) / add=dll(PE) / remove=a(ar)）
prepare_repo_executable modify so
prepare_repo_executable add dll
prepare_repo_executable remove a
# 证书 demo：三场景各用一种容器（modify=der / add=p12 / remove=p7b）
prepare_repo_certificate modify der
prepare_repo_certificate add p12
prepare_repo_certificate remove p7b
# 音频 demo：三场景各用一种容器（modify=mp3 / add=wav / remove=flac）
prepare_repo_audio modify mp3
prepare_repo_audio add wav
prepare_repo_audio remove flac
# 视频 demo：三场景各用一种容器（modify=mp4 / add=mkv / remove=avi）
prepare_repo_video modify mp4
prepare_repo_video add mkv
prepare_repo_video remove avi
start_x
# PDF 插件三种场景各截一张：修改 / 新增 / 删除
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-pdf-$mode" "PdfDiffView.SetContent" "pdf-$mode.png" 4 "PDF 插件 · $mode"
done
# Office 插件三种场景各截一张（Word / Excel / PowerPoint 各覆盖一种）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-office-$mode" "OfficeDiffView.SetContent" "office-$mode.png" 4 "Office 插件 · $mode"
done
# 压缩包插件三种场景各截一张（zip / tar.gz / tar.xz 各覆盖一种）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-archive-$mode" "ArchiveDiffView.SetContent" "archive-$mode.png" 4 "Archive 插件 · $mode"
done
# 字体插件三种场景各截一张：修改 / 新增 / 删除
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-font-$mode" "FontDiffView.SetContent" "font-$mode.png" 4 "字体插件 · $mode"
done
# 可执行文件插件三种场景各截一张（ELF(.so) / PE(.dll) / ar(.a) 各覆盖一种）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-executable-$mode" "ExecutableDiffView.SetContent" "executable-$mode.png" 4 "可执行文件插件 · $mode"
done
# 证书插件三种场景各截一张（DER / PKCS#12 / PKCS#7 各覆盖一种）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-certificate-$mode" "CertificateDiffView.SetContent" "certificate-$mode.png" 4 "证书插件 · $mode"
done
# 音频插件三种场景各截一张（mp3 / wav / flac 各覆盖一种；默认元数据模式）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-audio-$mode" "AudioDiffView.SetContent" "audio-$mode.png" 4 "音频插件 · $mode"
done
# 视频插件三种场景各截一张（mp4 / mkv / avi 各覆盖一种；默认元数据模式）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-video-$mode" "VideoDiffView.SetContent" "video-$mode.png" 4 "视频插件 · $mode"
done
write_metadata
log "完成：截图已输出到 $OUT"