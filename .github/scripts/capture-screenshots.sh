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
#   ④ 准备 demo 仓库：PDF / Office / 压缩包 / 字体 / 可执行文件 / 证书 / 音频 / 视频 /
#      结构化数据 / 字幕 / SVG 各三种 diff 场景（修改 / 新增 / 删除）
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
	# 三方件原生库：按 third_party/<component>/manifest.json 从三方件仓最新 Release 取件 +
	# 校验 sha256（不锁版本）。音视频插件按 RID 把原生库随包拷进输出根，故须在构建前完成。
	bash "$REPO_ROOT/.github/scripts/fetch-third-party.sh" "$RID"
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
	local freq dur rate codec cover_color
	if [ "$version" = "v2" ]; then
		freq=660
		dur=4
		rate=48000
		cover_color=0xcc6633
	else
		freq=440
		dur=3
		rate=44100
		cover_color=0x3366cc
	fi
	case "$ext" in
	wav) codec=pcm_s16le ;;
	flac) codec=flac ;;
	*) codec=libmp3lame ;;
	esac
	# mp3 额外内嵌一张随版本变色的封面（ID3 APIC），让「封面」模式的截图左右可辨；
	# 其它容器不支持内嵌封面，跳过。造图 / 合流失败则降级为无封面。
	if [ "$ext" = "mp3" ]; then
		local cover="$repo/.cover-$version.png"
		if ffmpeg -hide_banner -loglevel error -f lavfi \
			-i "color=c=$cover_color:s=300x300" -frames:v 1 -y "$cover"; then
			ffmpeg -hide_banner -loglevel error \
				-f lavfi -i "sine=frequency=$freq:duration=$dur" \
				-i "$cover" -map 0:a -map 1:v -c:v copy -disposition:v attached_pic \
				-c:a "$codec" -ac:a 2 -ar:a "$rate" \
				-metadata title="ForkPlus Demo $version" -metadata artist="ForkPlus" \
				-metadata album="Audio Demo" -metadata date="2026" \
				-y "$repo/sample.$ext"
			rm -f "$cover"
			echo "  demo audio -> $repo/sample.$ext ($version · 含内嵌封面)"
			return
		fi
		rm -f "$cover"
	fi
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

# ── demo 素材：结构化数据 / 字幕 / SVG（纯文本格式）────────────────────────────
# 这三类素材都是**纯文本**，而宿主只对**二进制**差异查询插件路由（文本差异固定由内置文本
# 编辑器渲染）。因此 demo 仓库用 .gitattributes 给样本加 `-diff` 属性，让 git 按二进制上报
# 差异——文件内容仍是合法文本（插件照常解析），但差异会走插件路由，截图才落到这三个插件上，
# 而不是内置文本编辑器（对照 README「Pages 截图约定」）。

# 结构化数据样本：三种场景各用一种格式，覆盖三条解析路径——
#   modify → .yaml（YamlDotNet）/ add → .json（System.Text.Json）/ remove → .toml（Tomlyn）
# v2 改 server.host / port / tls 与 logging.level、删掉 logging.file、features 多一项，
# 让「已变更 / 仅左 / 仅右」三类行都出现在截图里。
write_demo_structured() {
	local repo="$1" version="$2" ext="$3"
	python3 - "$repo" "$version" "$ext" <<'PY'
import os, sys
repo, version, ext = sys.argv[1], sys.argv[2], sys.argv[3]
is_new = version == "v2"

YAML_V1 = """server:
  host: localhost
  port: 8080
  tls: false
logging:
  level: info
  file: /var/log/forkplus.log
features:
  - export
  - sync
"""
YAML_V2 = """server:
  host: 0.0.0.0
  port: 8443
  tls: true
logging:
  level: debug
features:
  - export
  - sync
  - ai
"""
JSON_V1 = """{
  "name": "forkplus",
  "version": "1.0.0",
  "server": { "host": "localhost", "port": 8080, "tls": false },
  "logging": { "level": "info", "file": "/var/log/forkplus.log" },
  "features": ["export", "sync"]
}
"""
JSON_V2 = """{
  "name": "forkplus",
  "version": "2.0.0",
  "server": { "host": "0.0.0.0", "port": 8443, "tls": true },
  "logging": { "level": "debug" },
  "features": ["export", "sync", "ai"]
}
"""
TOML_V1 = """[server]
host = "localhost"
port = 8080
tls = false

[logging]
level = "info"
file = "/var/log/forkplus.log"

[features]
list = ["export", "sync"]
"""
TOML_V2 = """[server]
host = "0.0.0.0"
port = 8443
tls = true

[logging]
level = "debug"

[features]
list = ["export", "sync", "ai"]
"""

pairs = {
    "yaml": (YAML_V1, YAML_V2),
    "json": (JSON_V1, JSON_V2),
    "toml": (TOML_V1, TOML_V2),
}
try:
    old, new = pairs[ext]
except KeyError:
    raise SystemExit("unsupported demo structured ext: " + ext)
target = os.path.join(repo, "sample." + ext)
with open(target, "w", encoding="utf-8") as f:
    f.write(new if is_new else old)
print("  demo structured ->", target, "(" + version + ")")
PY
}

# DBC（CAN 数据库）样本：三场景共用同一对 .dbc（旧 / 新两版），覆盖节点 / 报文 / 信号 /
# 值表 / 属性等本插件解析的全部对象。
#   v1：2 报文 / 3 信号——EngineData（EngineSpeed + EngineTemp，EngineTemp 带值表）、
#       VehicleSpeed（WheelSpeed）；EngineData 挂 GenMsgCycleTime 属性，另有报文 / 信号注释。
#   v2：改 EngineSpeed 的因子（0.25→0.5）与取值范围、给 EngineData 加 EngineLoad、
#       删掉 WheelSpeed、新增报文 GearStatus（CurrentGear，带值表）与节点 Transmission——
#       让「已变更 / 仅左 / 仅右」三类行都出现在截图里。
write_demo_dbc() {
	local repo="$1" version="$2"
	python3 - "$repo" "$version" <<'PY'
import os, sys
repo, version = sys.argv[1], sys.argv[2]
is_new = version == "v2"

DBC_V1 = """VERSION ""

NS_ :
	CM_
	BA_DEF_
	BA_
	VAL_
	BA_DEF_DEF_

BS_:

BU_: Engine Gateway

BO_ 256 EngineData: 8 Engine
 SG_ EngineSpeed : 0|16@1+ (0.25,0) [0|16383.75] "rpm" Gateway
 SG_ EngineTemp : 16|8@1+ (1,-40) [-40|215] "degC" Gateway

BO_ 512 VehicleSpeed: 4 Gateway
 SG_ WheelSpeed : 0|16@1+ (0.01,0) [0|655.35] "km/h" Engine

CM_ BO_ 256 "Engine data frame";
CM_ SG_ 256 EngineSpeed "Engine crank speed";

BA_DEF_ BO_ "GenMsgCycleTime" INT 0 65535;
BA_DEF_DEF_ "GenMsgCycleTime" 0;
BA_ "GenMsgCycleTime" BO_ 256 10;

VAL_ 256 EngineTemp -40 "Cold" 100 "Hot" ;
"""

DBC_V2 = """VERSION ""

NS_ :
	CM_
	BA_DEF_
	BA_
	VAL_
	BA_DEF_DEF_

BS_:

BU_: Engine Gateway Transmission

BO_ 256 EngineData: 8 Engine
 SG_ EngineSpeed : 0|16@1+ (0.5,0) [0|8191.875] "rpm" Gateway
 SG_ EngineTemp : 16|8@1+ (1,-40) [-40|215] "degC" Gateway
 SG_ EngineLoad : 24|8@1+ (0.5,0) [0|127.5] "%" Gateway

BO_ 512 VehicleSpeed: 4 Gateway

BO_ 768 GearStatus: 2 Transmission
 SG_ CurrentGear : 0|8@1+ (1,0) [0|255] "" Engine

CM_ BO_ 256 "Engine data frame";
CM_ SG_ 256 EngineSpeed "Engine crank speed";

BA_DEF_ BO_ "GenMsgCycleTime" INT 0 65535;
BA_DEF_DEF_ "GenMsgCycleTime" 0;
BA_ "GenMsgCycleTime" BO_ 256 10;

VAL_ 256 EngineTemp -40 "Cold" 100 "Hot" ;
VAL_ 768 CurrentGear 0 "Neutral" 1 "Drive" 2 "Reverse" ;
"""

target = os.path.join(repo, "sample.dbc")
with open(target, "w", encoding="utf-8") as f:
    f.write(DBC_V2 if is_new else DBC_V1)
print("  demo dbc ->", target, "(" + version + ")")
PY
}

# 字幕样本：三种场景各用一种格式，覆盖三条解析路径——
#   modify → .srt / add → .vtt（WebVTT）/ remove → .ass（ASS）
# v2 把第 2 句改写并后移、末尾新增一条 cue，让「已变 / 仅右」与时间轴上的挪动都可见。
write_demo_subtitle() {
	local repo="$1" version="$2" ext="$3"
	python3 - "$repo" "$version" "$ext" <<'PY'
import os, sys
repo, version, ext = sys.argv[1], sys.argv[2], sys.argv[3]
is_new = version == "v2"

SRT_V1 = """1
00:00:01,000 --> 00:00:03,000
Hello and welcome.

2
00:00:03,500 --> 00:00:06,000
This is the old build.

3
00:00:06,500 --> 00:00:08,000
Shared line.
"""
SRT_V2 = """1
00:00:01,000 --> 00:00:03,000
Hello and welcome.

2
00:00:04,000 --> 00:00:06,500
This is the new build.

3
00:00:06,500 --> 00:00:08,000
Shared line.

4
00:00:09,000 --> 00:00:11,000
Brand new subtitle line.
"""
VTT_V2 = """WEBVTT

00:00:01.000 --> 00:00:03.000
Hello and welcome.

00:00:04.000 --> 00:00:06.500
This is the new build.

00:00:06.500 --> 00:00:08.000
Shared line.

00:00:09.000 --> 00:00:11.000
Brand new subtitle line.
"""
ASS_V1 = """[Script Info]
Title: ForkPlus Demo

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:01.00,0:00:03.00,Default,Old,0,0,0,,Hello and welcome.
Dialogue: 0,0:00:03.50,0:00:06.00,Default,Old,0,0,0,,This is the old build.
Dialogue: 0,0:00:06.50,0:00:08.00,Default,Old,0,0,0,,Shared line.
"""

texts = {
    "srt": SRT_V2 if is_new else SRT_V1,
    "vtt": VTT_V2,
    "ass": ASS_V1,
}
try:
    text = texts[ext]
except KeyError:
    raise SystemExit("unsupported demo subtitle ext: " + ext)
target = os.path.join(repo, "sample." + ext)
with open(target, "w", encoding="utf-8") as f:
    f.write(text)
print("  demo subtitle ->", target, "(" + version + ")")
PY
}

# SVG 样本：三场景共用同一对 .svg（旧：蓝底矩形 + 灰轴线；新：换色 + 矩形挪位 + 多一个圆 +
# 文字改版号），让并排渲染与结构 diff 两种模式都有明显可见的差异。
write_demo_svg() {
	local repo="$1" version="$2"
	python3 - "$repo" "$version" <<'PY'
import os, sys
repo, version = sys.argv[1], sys.argv[2]
is_new = version == "v2"

SVG_V1 = """<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="320" height="200" viewBox="0 0 320 200">
  <rect id="bg" x="20" y="30" width="120" height="80" fill="#3366cc"/>
  <line id="axis" x1="20" y1="150" x2="300" y2="150" stroke="#999999" stroke-width="2"/>
  <text id="label" x="20" y="180" font-size="14" fill="#333333">ForkPlus v1</text>
</svg>
"""
SVG_V2 = """<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="320" height="200" viewBox="0 0 320 200">
  <rect id="bg" x="80" y="30" width="120" height="80" fill="#e67e22"/>
  <line id="axis" x1="20" y1="150" x2="300" y2="150" stroke="#999999" stroke-width="2"/>
  <circle id="dot" cx="240" cy="70" r="28" fill="#2e9e5b"/>
  <text id="label" x="20" y="180" font-size="14" fill="#333333">ForkPlus v2</text>
</svg>
"""
target = os.path.join(repo, "sample.svg")
with open(target, "w", encoding="utf-8") as f:
    f.write(SVG_V2 if is_new else SVG_V1)
print("  demo svg ->", target, "(" + version + ")")
PY
}

# ── demo 素材：机器学习模型 / MIDI / Torrent / 抓包 / PSD / EPUB（二进制格式）────
# 这六类样本由纯 Python 标准库现场构造最小合法文件（PSD 另借系统 ffmpeg 造缩略图）。
# 文件里都夹带 NUL 字节，git 一律判为二进制——只有二进制差异才会走插件路由（见 README）。

# 机器学习模型：三场景各用一种格式（modify=onnx / add=safetensors / remove=gguf），
# 一次覆盖三种解析路径。样例按各自规范写出真实结构，且各含 NUL 字节。
write_demo_mlmodel() {
	local repo="$1" version="$2" ext="$3"
	python3 - "$repo" "$version" "$ext" <<'PY'
import json, os, struct, sys
repo, version, ext = sys.argv[1], sys.argv[2], sys.argv[3]
is_new = version == "v2"

def _uvarint(n):
    out = bytearray()
    while True:
        b = n & 0x7F
        n >>= 7
        if n:
            out.append(b | 0x80)
        else:
            out.append(b)
            return bytes(out)

def _tag(field, wire):
    return _uvarint((field << 3) | wire)

def _vint(field, value):
    return _tag(field, 0) + _uvarint(value)

def _str(field, text):
    data = text.encode("utf-8")
    return _tag(field, 2) + _uvarint(len(data)) + data

def _msg(field, payload):
    return _tag(field, 2) + _uvarint(len(payload)) + payload

def _onnx():
    # NodeProto：3=name、4=op_type
    def node(op, name):
        return _str(3, name) + _str(4, op)
    # TensorProto(initializer)：1=dims、2=data_type、8=name、9=raw_data（NUL 填充保证二进制判定）
    def initializer(name, dims, dtype):
        body = b"".join(_vint(1, d) for d in dims)
        body += _vint(2, dtype) + _str(8, name)
        body += _tag(9, 2) + _uvarint(16) + b"\x00" * 16
        return body
    # ValueInfoProto：1=name、2=TypeProto(Tensor：1=elem_type、2=shape)
    def value_info(name, elem_type, dims):
        shape = b"".join(_msg(1, _vint(1, d)) for d in dims)   # Dimension.dim_value
        tensor_type = _vint(1, elem_type) + _msg(2, shape)
        return _str(1, name) + _msg(2, _msg(1, tensor_type))
    if is_new:
        nodes = [node("Conv", "conv1"), node("Relu", "relu1"),
                 node("MatMul", "fc1"), node("Add", "bias"), node("Softmax", "prob")]
        inits = [initializer("conv1.weight", [16, 3, 3, 3], 1),
                 initializer("fc1.weight", [10, 144], 1),
                 initializer("fc1.bias", [10], 1)]
        producer_version, model_version = "1.15.0", 2
    else:
        nodes = [node("Conv", "conv1"), node("Relu", "relu1"), node("MatMul", "fc1")]
        inits = [initializer("conv1.weight", [8, 3, 3, 3], 1),
                 initializer("fc1.weight", [10, 72], 1)]
        producer_version, model_version = "1.14.0", 1
    graph = b"".join(_msg(1, n) for n in nodes)
    graph += _str(2, "forkplus_demo")
    graph += b"".join(_msg(5, t) for t in inits)
    graph += _msg(11, value_info("input", 1, [1, 3, 32, 32]))
    graph += _msg(12, value_info("output", 1, [1, 10]))
    model = _vint(1, 8) + _str(2, "forkplus-demo") + _str(3, producer_version)
    model += _vint(5, model_version) + _msg(7, graph)
    # doc_string（字段 14，解析器按未知字段跳过）：内容含 NUL，确保 git 判二进制
    model += _str(14, "ForkPlus demo model\x00")
    return model

def _safetensors():
    if is_new:
        tensors = [("model.embed_tokens.weight", "F32", [32, 64]),
                   ("model.layers.0.attn.q_proj.weight", "F16", [64, 64]),
                   ("model.layers.1.attn.q_proj.weight", "F16", [64, 64])]
        meta = {"format": "pt", "framework": "transformers", "revision": "v2"}
    else:
        tensors = [("model.embed_tokens.weight", "F32", [16, 32]),
                   ("model.layers.0.attn.q_proj.weight", "F16", [32, 32])]
        meta = {"format": "pt", "framework": "transformers", "revision": "v1"}
    size_of = {"F32": 4, "F16": 2}
    header = {"__metadata__": meta}
    offset = 0
    for name, dtype, shape in tensors:
        count = 1
        for d in shape:
            count *= d
        size = count * size_of[dtype]
        header[name] = {"dtype": dtype, "shape": shape, "data_offsets": [offset, offset + size]}
        offset += size
    blob = json.dumps(header, separators=(",", ":")).encode("utf-8")
    return struct.pack("<Q", len(blob)) + blob + b"\x00" * offset

def _gguf():
    def gstr(text):
        data = text.encode("utf-8")
        return struct.pack("<Q", len(data)) + data
    if is_new:
        kvs = [("general.architecture", 8, "llama"),
               ("general.name", 8, "ForkPlus Demo v2"),
               ("llama.context_length", 4, 4096),
               ("llama.embedding_length", 4, 128)]
        tensors = [("token_embd.weight", [64, 128], 0, 0),
                   ("blk.0.attn_q.weight", [128, 128], 2, 8192),
                   ("blk.1.attn_q.weight", [128, 128], 2, 16384)]
    else:
        kvs = [("general.architecture", 8, "llama"),
               ("general.name", 8, "ForkPlus Demo v1"),
               ("llama.context_length", 4, 2048),
               ("llama.embedding_length", 4, 64)]
        tensors = [("token_embd.weight", [32, 64], 0, 0),
                   ("blk.0.attn_q.weight", [64, 64], 1, 4096)]
    out = bytearray(b"GGUF") + struct.pack("<I", 3)
    out += struct.pack("<Q", len(tensors)) + struct.pack("<Q", len(kvs))
    for key, vtype, value in kvs:
        out += gstr(key) + struct.pack("<I", vtype)
        out += gstr(value) if vtype == 8 else struct.pack("<I", value)
    for name, dims, ttype, offset in tensors:
        out += gstr(name) + struct.pack("<I", len(dims))
        for d in dims:
            out += struct.pack("<Q", d)
        out += struct.pack("<I", ttype) + struct.pack("<Q", offset)
    return bytes(out)

data = {"onnx": _onnx, "safetensors": _safetensors, "gguf": _gguf}[ext]()
target = os.path.join(repo, "sample." + ext)
with open(target, "wb") as f:
    f.write(data)
print("  demo mlmodel ->", target, "(" + version + " · " + ext + ")")
PY
}

# MIDI：三场景共用同一对 .mid（旧：C 大调三音；新：加音 / 改力度 / 改 tempo），
# varlen delta 与 note 字节天然含 NUL，git 判二进制。
write_demo_midi() {
	local repo="$1" version="$2"
	python3 - "$repo" "$version" <<'PY'
import os, struct, sys
repo, version = sys.argv[1], sys.argv[2]
is_new = version == "v2"

def varlen(n):
    out = bytearray([n & 0x7F])
    n >>= 7
    while n:
        out.insert(0, (n & 0x7F) | 0x80)
        n >>= 7
    return bytes(out)

def event(delta, payload):
    return varlen(delta) + payload

tempo = 600000 if is_new else 500000
notes = [(60, 100, 0, 480), (64, 90, 480, 480), (67, 100, 960, 480)]
if is_new:
    notes = [(60, 110, 0, 480), (64, 90, 480, 480), (67, 100, 960, 480), (72, 120, 1440, 960)]

events = [event(0, bytes([0xFF, 0x51, 0x03]) + tempo.to_bytes(3, "big"))]
name = b"ForkPlus Demo v2" if is_new else b"ForkPlus Demo v1"
events.append(event(0, bytes([0xFF, 0x03]) + varlen(len(name)) + name))

flattened = []
for pitch, vel, start, dur in notes:
    flattened.append((start, bytes([0x90, pitch, vel])))
    flattened.append((start + dur, bytes([0x80, pitch, 0x40])))
flattened.sort(key=lambda item: item[0])
last = 0
for tick, payload in flattened:
    events.append(event(tick - last, payload))
    last = tick
events.append(event(0, bytes([0xFF, 0x2F, 0x00])))

body = b"".join(events)
data = b"MThd" + struct.pack(">IHHH", 6, 1, 1, 480) + b"MTrk" + struct.pack(">I", len(body)) + body
target = os.path.join(repo, "sample.mid")
with open(target, "wb") as f:
    f.write(data)
print("  demo midi ->", target, "(" + version + ")")
PY
}

# Torrent：三场景共用同一对 .torrent（旧：1 tracker / 4 分片；新：加 tracker、换分片数、改长度），
# info.pieces 是二进制 SHA-1 串，含 NUL，git 判二进制。
write_demo_torrent() {
	local repo="$1" version="$2"
	python3 - "$repo" "$version" <<'PY'
import os, sys
repo, version = sys.argv[1], sys.argv[2]
is_new = version == "v2"

def bencode(value):
    if isinstance(value, bytes):
        return str(len(value)).encode() + b":" + value
    if isinstance(value, str):
        return bencode(value.encode("utf-8"))
    if isinstance(value, bool):
        return bencode(1 if value else 0)
    if isinstance(value, int):
        return b"i" + str(value).encode() + b"e"
    if isinstance(value, list):
        return b"l" + b"".join(bencode(v) for v in value) + b"e"
    if isinstance(value, dict):
        out = b"d"
        for k in sorted(value.keys()):
            out += bencode(k) + bencode(value[k])
        return out + b"e"
    raise TypeError(type(value))

def pieces(count, seed):
    # 20 字节 × count 的伪分片串：含 NUL，且随版本变化
    return bytes((seed + i * 37) % 256 for i in range(20 * count))

if is_new:
    name, length, count, seed = "forkplus-demo-v2", 33554432, 6, 200
    announce_list = [["https://tracker.forkplus.local/announce"],
                     ["udp://tracker2.forkplus.local:6969/announce"]]
else:
    name, length, count, seed = "forkplus-demo-v1", 16777216, 4, 10
    announce_list = [["https://tracker.forkplus.local/announce"]]

torrent = {
    "announce": "https://tracker.forkplus.local/announce",
    "announce-list": announce_list,
    "comment": "ForkPlus demo torrent",
    "created by": "ForkPlus Pages",
    "encoding": "UTF-8",
    "info": {
        "name": name,
        "length": length,
        "piece length": 262144,
        "pieces": pieces(count, seed),
        "private": 1,
        "source": "ForkPlus Demo " + version,
    },
}
target = os.path.join(repo, "sample.torrent")
with open(target, "wb") as f:
    f.write(bencode(torrent))
print("  demo torrent ->", target, "(" + version + ")")
PY
}

# 网络抓包：三场景 modify/remove 用 .pcap、add 用 .pcapng。造 Ethernet + IPv4 + TCP/UDP/ICMP
# 的真实帧，v2 多加一条 TCP 会话与一条 NTP 会话，让协议分布与 Top 会话都有差异。
write_demo_pcap() {
	local repo="$1" version="$2" ext="$3"
	python3 - "$repo" "$version" "$ext" <<'PY'
import os, struct, sys
repo, version, ext = sys.argv[1], sys.argv[2], sys.argv[3]
is_new = version == "v2"

A, B, C, D, E, F = "10.0.0.5", "10.0.0.1", "8.8.8.8", "10.0.0.9", "10.0.0.7", "10.0.0.2"

def mac_addr(text):
    return bytes(int(b, 16) for b in text.split(":"))

def ip_bytes(addr):
    return bytes(int(b) for b in addr.split("."))

def ipv4(proto, src, dst, payload):
    total = 20 + len(payload)
    return (bytes([0x45, 0x00]) + struct.pack(">H", total) + b"\x00\x01\x00\x00"
            + bytes([64, proto]) + b"\x00\x00" + ip_bytes(src) + ip_bytes(dst) + payload)

def tcp(src, dst, sport, dport, seq, flags, payload=b""):
    seg = struct.pack(">HHIIBBHHH", sport, dport, seq, 0, 0x50, flags, 64240, 0, 0) + payload
    return ipv4(6, src, dst, seg)

def udp(src, dst, sport, dport, payload=b""):
    return ipv4(17, src, dst, struct.pack(">HHHH", sport, dport, 8 + len(payload), 0) + payload)

def icmp(src, dst):
    return ipv4(1, src, dst, bytes([8, 0, 0, 0, 0x12, 0x34, 0x00, 0x01]))

def frame(inner):
    eth = mac_addr("02:00:00:00:00:01") + mac_addr("02:00:00:00:00:02") + b"\x08\x00" + inner
    return eth + b"\x00" * max(0, 60 - len(eth))

dns = b"\x12\x34\x01\x00\x00\x01\x00\x00\x00\x00\x00\x00"
if is_new:
    packets = [(0, frame(tcp(A, B, 49152, 443, 3000, 0x02))),
               (100000, frame(tcp(B, A, 443, 49152, 4000, 0x12))),
               (200000, frame(tcp(A, B, 49152, 443, 3001, 0x10))),
               (300000, frame(tcp(E, F, 52000, 80, 10, 0x02))),
               (400000, frame(tcp(E, F, 52000, 80, 11, 0x10))),
               (500000, frame(udp(A, C, 5353, 53, dns))),
               (600000, frame(udp(E, C, 40000, 123, b"\x1b" + b"\x00" * 47))),
               (700000, frame(icmp(A, D)))]
else:
    packets = [(0, frame(tcp(A, B, 49152, 443, 1000, 0x02))),
               (120000, frame(tcp(B, A, 443, 49152, 2000, 0x12))),
               (240000, frame(tcp(A, B, 49152, 443, 1001, 0x10))),
               (360000, frame(udp(A, C, 5353, 53, dns))),
               (480000, frame(icmp(A, D)))]

def build_pcap():
    out = bytearray(struct.pack("<IHHiIII", 0xA1B2C3D4, 2, 4, 0, 0, 262144, 1))
    for offset_us, fr in packets:
        out += struct.pack("<IIII", offset_us // 1000000, offset_us % 1000000, len(fr), len(fr)) + fr
    return bytes(out)

def build_pcapng():
    def block(btype, body):
        body += b"\x00" * ((4 - len(body) % 4) % 4)
        total = 12 + len(body)
        return struct.pack(">II", btype, total) + body + struct.pack(">I", total)
    shb = block(0x0A0D0D0A, struct.pack(">IHHq", 0x1A2B3C4D, 1, 0, -1))
    idb = block(0x00000001, struct.pack(">HHI", 1, 0, 262144) + struct.pack(">HH", 9, 1) + b"\x06\x00\x00\x00" + struct.pack(">HH", 0, 0))
    out = bytearray(shb + idb)
    for offset_us, fr in packets:
        high, low = divmod(offset_us, 1 << 32)
        body = struct.pack(">IIIII", 0, high, low, len(fr), len(fr)) + fr
        out += block(0x00000006, body)
    return bytes(out)

data = build_pcapng() if ext == "pcapng" else build_pcap()
target = os.path.join(repo, "sample." + ext)
with open(target, "wb") as f:
    f.write(data)
print("  demo pcap ->", target, "(" + version + " · " + ext + ")")
PY
}

# PSD / PSB：三场景 modify/remove 用 .psd、add 用 .psb。头部 + 图像资源段（1036 缩略图，
# 用系统 ffmpeg 造的小 JPEG）+ 图层与蒙版段（图层记录带 luni / LSct）+ 图像数据段压缩标记。
# v2 改缩略图颜色 / 挪图层 / 改混合模式与不透明度 / 增删图层 / 把分组改成闭合。
write_demo_psd() {
	local repo="$1" version="$2" ext="$3"
	local color thumb
	if [ "$version" = "v2" ]; then color=0xe67e22; else color=0x3366cc; fi
	thumb="$repo/.thumb-$version.jpg"
	ffmpeg -hide_banner -loglevel error -f lavfi -i "color=c=$color:s=96x96" -frames:v 1 -y "$thumb" 2>/dev/null || rm -f "$thumb"
	python3 - "$repo" "$version" "$ext" "$thumb" <<'PY'
import os, struct, sys
repo, version, ext, thumb = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
is_new = version == "v2"
psb = ext == "psb"
W = H = 256

def u16(v):
    return struct.pack(">H", v & 0xFFFF)

def i16(v):
    return struct.pack(">h", v)

def u32(v):
    return struct.pack(">I", v & 0xFFFFFFFF)

def i32(v):
    return struct.pack(">i", v)

def section_len(v):
    return struct.pack(">Q", v) if psb else u32(v)

def pascal_name(name):
    data = name.encode("latin-1", "replace")
    out = bytes([len(data)]) + data
    align = 4 if psb else 2
    while len(out) % align != 0:
        out += b"\x00"
    return out

def additional_block(key, payload):
    return b"8BIM" + key + u32(len(payload)) + payload

def luni(name):
    return additional_block(b"luni", i32(len(name)) + name.encode("utf-16-be"))

def lsct(kind):
    return additional_block(b"LSct", i32(kind))

def layer_record(name, rect, blend, opacity, visible, channels, section=None):
    top, left, bottom, right = rect
    rec = i32(top) + i32(left) + i32(bottom) + i32(right) + u16(len(channels))
    for cid in channels:
        rec += i16(cid) + section_len(2)   # 通道数据长度（解析器只跳过）
    rec += b"8BIM" + blend + bytes([opacity, 0, 0 if visible else 2, 0])
    extra = u32(0) + u32(0) + pascal_name(name) + luni(name)
    if section is not None:
        extra += lsct(section)
    return rec + section_len(len(extra)) + extra

if is_new:
    layers = [
        layer_record("</Layer group>", (0, 0, H, W), b"norm", 100, True, [0, 1, 2], 3),
        layer_record("Effects", (0, 0, H, W), b"norm", 100, True, [0, 1, 2], 2),
        layer_record("Watermark", (96, 96, 224, 224), b"norm", 40, False, [0, 1, 2]),
        layer_record("Logo", (48, 48, 176, 176), b"mult", 100, True, [0, 1, 2]),
        layer_record("Background", (0, 0, H, W), b"norm", 100, True, [0, 1, 2]),
    ]
else:
    layers = [
        layer_record("</Layer group>", (0, 0, H, W), b"norm", 100, True, [0, 1, 2], 3),
        layer_record("Glow", (64, 64, 192, 192), b"scrn", 60, True, [0, 1, 2]),
        layer_record("Effects", (0, 0, H, W), b"norm", 100, True, [0, 1, 2], 1),
        layer_record("Logo", (32, 32, 160, 160), b"norm", 80, True, [0, 1, 2]),
        layer_record("Background", (0, 0, H, W), b"norm", 100, True, [0, 1, 2]),
    ]

def resource(rid, payload):
    out = b"8BIM" + u16(rid) + b"\x00\x00" + u32(len(payload)) + payload
    if len(payload) % 2 == 1:
        out += b"\x00"
    return out

resources = b""
jfif = open(thumb, "rb").read() if os.path.exists(thumb) else b""
if jfif:
    resources += resource(1036, u32(1) + u32(96) + u32(96) + b"\x00" * 16 + jfif)
if is_new:
    resources += resource(1005, struct.pack(">HHHHI", 72, 1, 72, 1, 1))   # ResolutionInfo

layer_info = i16(len(layers)) + b"".join(layers)
layer_mask = section_len(len(layer_info)) + layer_info

header = (b"8BPS" + struct.pack(">H", 2 if psb else 1) + b"\x00" * 6
          + u16(3) + u32(H) + u32(W) + u16(8) + u16(3))
data = (header + u32(0)                                   # 文件头 + 颜色模式数据段
        + u32(len(resources)) + resources                 # 图像资源段
        + section_len(len(layer_mask)) + layer_mask       # 图层与蒙版段
        + u16(0))                                         # 图像数据段压缩标记（none）
target = os.path.join(repo, "sample." + ext)
with open(target, "wb") as f:
    f.write(data)
print("  demo psd ->", target, "(" + version + " · " + ext + ")")
PY
	rm -f "$thumb"
}

# EPUB：三场景共用同一对 .epub（ZIP 容器：mimetype → META-INF/container.xml → OPF + nav + 章节）。
# v2 改书名 / 加作者 / 改标识与日期 / 改章节标题并新增一章；内嵌 cover.png 含 NUL，git 判二进制。
write_demo_epub() {
	local repo="$1" version="$2"
	python3 - "$repo" "$version" <<'PY'
import base64, os, sys, zipfile
repo, version = sys.argv[1], sys.argv[2]
is_new = version == "v2"

COVER_PNG = base64.b64decode(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")

def chapter_xhtml(title, body):
    return ("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>" + title + "</title></head>"
            "<body><h1>" + title + "</h1><p>" + body + "</p></body></html>\n")

if is_new:
    meta = {"title": "ForkPlus Handbook, 2nd Edition",
            "creators": ["Alice Author", "Bob Editor"],
            "identifier": "urn:uuid:forkplus-demo-v2",
            "date": "2026-06-01",
            "modified": "2026-06-01T00:00:00Z"}
    chapters = [("chap1.xhtml", "Chapter 1: Getting Started", "Install ForkPlus and open a repository."),
                ("chap2.xhtml", "Chapter 2: Comparing Models", "Compare ONNX, GGUF and SafeTensors side by side."),
                ("chap3.xhtml", "Chapter 3: Sharing Results", "Export the diff view and share the pages.")]
else:
    meta = {"title": "ForkPlus Handbook",
            "creators": ["Alice Author"],
            "identifier": "urn:uuid:forkplus-demo-v1",
            "date": "2026-01-01",
            "modified": "2026-01-01T00:00:00Z"}
    chapters = [("chap1.xhtml", "Chapter 1: Getting Started", "Install ForkPlus and open a repository."),
                ("chap2.xhtml", "Chapter 2: Comparing Files", "Compare PDF, Office and images side by side.")]

creators = "".join("<dc:creator>" + c + "</dc:creator>" for c in meta["creators"])
manifest = '<item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>\n'
manifest += "".join('<item id="%s" href="%s" media-type="application/xhtml+xml"/>\n' % (name[:-6], name)
                    for name, _, _ in chapters)
manifest += '<item id="cover" href="images/cover.png" media-type="image/png" properties="cover-image"/>'
spine = "".join('<itemref idref="%s"/>' % (name[:-6]) for name, _, _ in chapters)
links = "".join('<li><a href="%s">%s</a></li>' % (name, title) for name, title, _ in chapters)

container = ("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
             "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">"
             "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" "
             "media-type=\"application/oebps-package+xml\"/></rootfiles></container>\n")
opf = ("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
       "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bookid\">"
       "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">"
       "<dc:title>" + meta["title"] + "</dc:title>" + creators +
       "<dc:language>en</dc:language>"
       "<dc:identifier id=\"bookid\">" + meta["identifier"] + "</dc:identifier>"
       "<dc:date>" + meta["date"] + "</dc:date>"
       "<dc:publisher>ForkPlus Press</dc:publisher>"
       "<dc:description>A short demo book used by the ForkPlus Pages screenshots.</dc:description>"
       "<meta property=\"dcterms:modified\">" + meta["modified"] + "</meta>"
       "</metadata><manifest>" + manifest + "</manifest>"
       "<spine>" + spine + "</spine></package>\n")
nav = ("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
       "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">"
       "<head><title>Contents</title></head><body>"
       "<nav epub:type=\"toc\"><ol>" + links + "</ol></nav></body></html>\n")

target = os.path.join(repo, "sample.epub")
with zipfile.ZipFile(target, "w") as zf:
    zf.writestr(zipfile.ZipInfo("mimetype"), "application/epub+zip", compress_type=zipfile.ZIP_STORED)
    zf.writestr("META-INF/container.xml", container)
    zf.writestr("OEBPS/content.opf", opf)
    zf.writestr("OEBPS/nav.xhtml", nav)
    for name, title, body in chapters:
        zf.writestr("OEBPS/" + name, chapter_xhtml(title, body))
    zf.writestr("OEBPS/images/cover.png", COVER_PNG)
print("  demo epub ->", target, "(" + version + ")")
PY
}

# 三类文本格式 demo 仓库共用一套骨架：
#   ① 写 .gitattributes 给样本加 `-diff`——宿主只对二进制差异查询插件路由，这样样本虽仍是
#      合法文本，差异却会走插件（否则截图会落到内置文本编辑器上）；
#   ② 按 modify / add / remove 三种变更提交。writer 签名统一为 <repo> <version> [ext]。
prepare_repo_text() {
	local kind="$1" mode="$2" sample="$3" writer="$4" ext="${5:-}"
	local repo="$WORK/repo-$kind-$mode"
	init_demo_repo "$repo"
	printf '%s -diff\n' "$sample" >"$repo/.gitattributes"
	case "$mode" in
	add)
		printf 'baseline\n' >"$repo/readme.txt"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: baseline"
		"$writer" "$repo" v2 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "add: $sample"
		;;
	remove)
		printf 'baseline\n' >"$repo/readme.txt"
		"$writer" "$repo" v1 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add $sample"
		git -C "$repo" rm -q "$sample"
		git -C "$repo" commit -q -m "remove: delete $sample"
		;;
	*)
		"$writer" "$repo" v1 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add $sample"
		"$writer" "$repo" v2 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "update: modify $sample"
		;;
	esac
	echo "  $repo ($mode · $sample)"
}

# 二进制格式 demo 仓库骨架：与 prepare_repo_text 同构，但样本自带 NUL 字节，git 自动判为二进制，
# 无需 .gitattributes 干预（宿主只对二进制差异查询插件路由）。样本名固定 sample.<ext>，
# 仓库名 repo-<kind>-<mode>；一次覆盖「改 / 增 / 删」三种变更场景。
prepare_repo_binary() {
	local kind="$1" mode="$2" writer="$3" ext="$4"
	local repo="$WORK/repo-$kind-$mode"
	init_demo_repo "$repo"
	case "$mode" in
	add)
		printf 'baseline\n' >"$repo/readme.txt"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: baseline"
		"$writer" "$repo" v2 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "add: sample.$ext"
		;;
	remove)
		printf 'baseline\n' >"$repo/readme.txt"
		"$writer" "$repo" v1 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.$ext"
		git -C "$repo" rm -q "sample.$ext"
		git -C "$repo" commit -q -m "remove: delete sample.$ext"
		;;
	*)
		"$writer" "$repo" v1 "$ext"
		git -C "$repo" add -A
		git -C "$repo" commit -q -m "initial: add sample.$ext"
		"$writer" "$repo" v2 "$ext"
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
	local repo="$1" marker="$2" outfile="$3" settle="$4" label="$5" plugin_mode="${6:-}"
	log "启动 ForkPlus 打开 $repo（$label）"
	mkdir -p "$OUT"
	# plugin_mode 非空时经 FORKPLUS_PLUGIN_VIEW_MODE 指定插件的初始视图模式
	# （音频 波形 / 频谱 / 封面，视频 帧条 / 单帧对比 / 播放，
	#  结构化数据 tree，字幕 timeline，SVG structure，
	#  机器学习模型 tensors，MIDI pianoroll，抓包 packets，PSD layers / header，EPUB chapters）；
	# 不设则走各插件默认模式。
	( cd "$APPDIR" && DISPLAY="$DISPLAY_NUM" FORKPLUS_PLUGIN_VIEW_MODE="$plugin_mode" \
		./ForkPlus "$repo" >"$WORK/forkplus.log" 2>&1 & )

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
# 结构化数据 demo：三场景各用一种格式（modify=yaml / add=json / remove=toml）；
# 样本是纯文本，靠 .gitattributes 的 `-diff` 让差异走插件而非内置文本编辑器。
prepare_repo_text structured modify "sample.yaml" write_demo_structured yaml
prepare_repo_text structured add "sample.json" write_demo_structured json
prepare_repo_text structured remove "sample.toml" write_demo_structured toml
# DBC demo：三场景共用同一对 .dbc（旧：2 报文 / 3 信号带值表；新：改因子与取值范围、
# 加一条信号与一条报文、删一条信号）；样本是纯文本，靠 .gitattributes 的 `-diff` 走插件路由。
prepare_repo_text dbc modify "sample.dbc" write_demo_dbc
prepare_repo_text dbc add "sample.dbc" write_demo_dbc
prepare_repo_text dbc remove "sample.dbc" write_demo_dbc
# 字幕 demo：三场景各用一种格式（modify=srt / add=vtt / remove=ass）
prepare_repo_text subtitle modify "sample.srt" write_demo_subtitle srt
prepare_repo_text subtitle add "sample.vtt" write_demo_subtitle vtt
prepare_repo_text subtitle remove "sample.ass" write_demo_subtitle ass
# SVG demo：三场景共用同一对 .svg（旧 / 新各有明显图形差异）
prepare_repo_text svg modify "sample.svg" write_demo_svg
prepare_repo_text svg add "sample.svg" write_demo_svg
prepare_repo_text svg remove "sample.svg" write_demo_svg
# 机器学习模型 demo：三场景各用一种格式（modify=onnx / add=safetensors / remove=gguf），
# 一次覆盖三条解析路径；样本自带 NUL，git 判二进制，自动走插件路由。
prepare_repo_binary mlmodel modify write_demo_mlmodel onnx
prepare_repo_binary mlmodel add write_demo_mlmodel safetensors
prepare_repo_binary mlmodel remove write_demo_mlmodel gguf
# MIDI demo：三场景共用同一对 .mid（旧：C 大调三音；新：加音 / 改力度 / 改 tempo）
prepare_repo_binary midi modify write_demo_midi mid
prepare_repo_binary midi add write_demo_midi mid
prepare_repo_binary midi remove write_demo_midi mid
# Torrent demo：三场景共用同一对 .torrent（旧：1 tracker / 4 分片；新：加 tracker、换分片数、改长度）
prepare_repo_binary torrent modify write_demo_torrent torrent
prepare_repo_binary torrent add write_demo_torrent torrent
prepare_repo_binary torrent remove write_demo_torrent torrent
# 抓包 demo：三场景 modify/remove 用 .pcap、add 用 .pcapng
prepare_repo_binary pcap modify write_demo_pcap pcap
prepare_repo_binary pcap add write_demo_pcap pcapng
prepare_repo_binary pcap remove write_demo_pcap pcap
# PSD demo：三场景 modify/remove 用 .psd、add 用 .psb（缩略图借系统 ffmpeg 造）
prepare_repo_binary psd modify write_demo_psd psd
prepare_repo_binary psd add write_demo_psd psb
prepare_repo_binary psd remove write_demo_psd psd
# EPUB demo：三场景共用同一对 .epub（ZIP 容器 + OPF 元数据 + 章节）
prepare_repo_binary epub modify write_demo_epub epub
prepare_repo_binary epub add write_demo_epub epub
prepare_repo_binary epub remove write_demo_epub epub
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
# 音频插件另取三个可视化模式各一张（波形 / 频谱 / 封面），均以 modify 场景为样本
for m in "waveform:波形" "spectrum:频谱" "cover:封面"; do
	seed_settings
	capture_one "$WORK/repo-audio-modify" "AudioDiffView.SetContent" "audio-${m%%:*}.png" 5 "音频插件 · ${m##*:}" "${m%%:*}"
done
# 视频插件三种场景各截一张（mp4 / mkv / avi 各覆盖一种；默认元数据模式）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-video-$mode" "VideoDiffView.SetContent" "video-$mode.png" 4 "视频插件 · $mode"
done
# 视频插件另取三个模式各一张（帧条 / 单帧对比 / 播放），均以 modify 场景为样本；
# 播放模式要等后台解码建流并出首帧，settle 放大。
for m in "filmstrip:帧条" "frame:单帧对比" "playback:播放"; do
	seed_settings
	capture_one "$WORK/repo-video-modify" "VideoDiffView.SetContent" "video-${m%%:*}.png" 6 "视频插件 · ${m##*:}" "${m%%:*}"
done
# 结构化数据插件三种场景各截一张（yaml / json / toml 各覆盖一种；默认键路径模式）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-structured-$mode" "StructuredDiffView.SetContent" "structured-$mode.png" 4 "结构化数据插件 · $mode"
done
# 结构化数据插件另取结构树模式一张（modify 场景为样本）
seed_settings
capture_one "$WORK/repo-structured-modify" "StructuredDiffView.SetContent" "structured-tree.png" 4 "结构化数据插件 · 结构树" "tree"
# DBC 插件三种场景各截一张（旧 / 新同一对 .dbc；默认结构化键路径模式）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-dbc-$mode" "DbcDiffView.SetContent" "dbc-$mode.png" 4 "DBC 插件 · $mode"
done
# DBC 插件另取原文模式一张（modify 场景为样本）
seed_settings
capture_one "$WORK/repo-dbc-modify" "DbcDiffView.SetContent" "dbc-raw.png" 4 "DBC 插件 · 原文" "raw"
# 字幕插件三种场景各截一张（srt / vtt / ass 各覆盖一种；默认字幕行表模式）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-subtitle-$mode" "SubtitleDiffView.SetContent" "subtitle-$mode.png" 4 "字幕插件 · $mode"
done
# 字幕插件另取时间轴模式一张（modify 场景为样本）
seed_settings
capture_one "$WORK/repo-subtitle-modify" "SubtitleDiffView.SetContent" "subtitle-timeline.png" 4 "字幕插件 · 时间轴" "timeline"
# SVG 插件三种场景各截一张（旧 / 新同一对 .svg；默认并排渲染模式）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-svg-$mode" "SvgDiffView.SetContent" "svg-$mode.png" 4 "SVG 插件 · $mode"
done
# SVG 插件另取结构差异模式一张（modify 场景为样本）
seed_settings
capture_one "$WORK/repo-svg-modify" "SvgDiffView.SetContent" "svg-structure.png" 4 "SVG 插件 · 结构差异" "structure"
# 机器学习模型插件三种场景各截一张（onnx / safetensors / gguf 各覆盖一种；默认元数据模式）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-mlmodel-$mode" "MlModelDiffView.SetContent" "mlmodel-$mode.png" 4 "机器学习模型插件 · $mode"
done
# 机器学习模型插件另取张量模式一张（modify 场景为样本）
seed_settings
capture_one "$WORK/repo-mlmodel-modify" "MlModelDiffView.SetContent" "mlmodel-tensors.png" 4 "机器学习模型插件 · 张量" "tensors"
# MIDI 插件三种场景各截一张（旧 / 新同一对 .mid；默认音符表模式）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-midi-$mode" "MidiDiffView.SetContent" "midi-$mode.png" 4 "MIDI 插件 · $mode"
done
# MIDI 插件另取钢琴卷帘模式一张（modify 场景为样本）
seed_settings
capture_one "$WORK/repo-midi-modify" "MidiDiffView.SetContent" "midi-pianoroll.png" 4 "MIDI 插件 · 钢琴卷帘" "pianoroll"
# Torrent 插件三种场景各截一张（旧 / 新同一对 .torrent；单一键路径表模式）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-torrent-$mode" "TorrentDiffView.SetContent" "torrent-$mode.png" 4 "Torrent 插件 · $mode"
done
# 抓包插件三种场景各截一张（modify/remove=pcap、add=pcapng；默认统计模式）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-pcap-$mode" "PcapDiffView.SetContent" "pcap-$mode.png" 4 "抓包插件 · $mode"
done
# 抓包插件另取包列表模式一张（modify 场景为样本）
seed_settings
capture_one "$WORK/repo-pcap-modify" "PcapDiffView.SetContent" "pcap-packets.png" 4 "抓包插件 · 包列表" "packets"
# PSD 插件三种场景各截一张（modify/remove=psd、add=psb；默认预览模式）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-psd-$mode" "PsdDiffView.SetContent" "psd-$mode.png" 4 "PSD 插件 · $mode"
done
# PSD 插件另取图层 / 头部两模式各一张（modify 场景为样本）
for m in "layers:图层" "header:头部"; do
	seed_settings
	capture_one "$WORK/repo-psd-modify" "PsdDiffView.SetContent" "psd-${m%%:*}.png" 4 "PSD 插件 · ${m##*:}" "${m%%:*}"
done
# EPUB 插件三种场景各截一张（旧 / 新同一对 .epub；默认元数据模式）
for mode in modify add remove; do
	seed_settings
	capture_one "$WORK/repo-epub-$mode" "EpubDiffView.SetContent" "epub-$mode.png" 4 "EPUB 插件 · $mode"
done
# EPUB 插件另取章节目录模式一张（modify 场景为样本）
seed_settings
capture_one "$WORK/repo-epub-modify" "EpubDiffView.SetContent" "epub-chapters.png" 4 "EPUB 插件 · 章节目录" "chapters"
write_metadata
log "完成：截图已输出到 $OUT"