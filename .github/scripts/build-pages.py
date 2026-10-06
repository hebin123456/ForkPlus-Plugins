#!/usr/bin/env python3
"""把截图与元数据注入 Pages 模板，生成「总览页 + 每个插件一个子页」的静态站点。

输入：
  .github/pages/template-index.html   总览页模板（通用说明 + 插件卡片）
  .github/pages/template-plugin.html  插件子页模板
  .github/pages/plugins.json          插件登记表（id / 名称 / 说明 / 扩展名 / 截图 / 章节）
  .github/pages/style.css             样式表（亮色）
  <pages>/assets/*.png                capture-screenshots.sh 产出的截图
  <pages>/assets/meta.json            ForkPlus / 插件版本与生成时间

输出：
  <pages>/index.html                  总览页
  <pages>/plugins/<id>.html           各插件子页
  <pages>/style.css
"""
import json
import os
import re
import shutil
import sys

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEMPLATE_DIR = os.path.join(REPO_ROOT, ".github", "pages")
PAGES_DIR = os.environ.get("PAGES_DIR", os.path.join(REPO_ROOT, "pages"))
ASSETS_DIR = os.path.join(PAGES_DIR, "assets")


def fail(msg):
    print(f"ERROR: {msg}", file=sys.stderr)
    sys.exit(1)


def load_json(path, default=None):
    if not os.path.exists(path):
        return default
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def render(template, values):
    """单遍替换 {{KEY}}；不重扫替换结果，键值里出现的 {{...}} 不会被二次展开。"""
    return re.sub(r"\{\{(\w+)\}\}", lambda m: str(values.get(m.group(1), m.group(0))), template)


def ext_codes(exts):
    return "".join(f'<code>{e}</code>' for e in exts)


def plugin_card(plugin):
    version = plugin.get("version")
    version_html = f'  <p class="ver">版本 v{version}</p>\n' if version else ''
    return (
        f'<a class="card plugin-card" href="plugins/{plugin["id"]}.html">\n'
        f'  <h3>{plugin["name"]}</h3>\n'
        f'{version_html}'
        f'  <p>{plugin["summary"]}</p>\n'
        f'  <p class="exts">{ext_codes(plugin.get("extensions", []))}</p>\n'
        f'  <span class="more">查看对比视图效果 →</span>\n'
        f'</a>'
    )


def plugin_screenshots(plugin):
    blocks = []
    for shot in plugin.get("screenshots", []):
        src = f'../assets/{shot["file"]}'
        blocks.append(
            f'<figure>\n'
            f'  <a href="{src}" target="_blank" rel="noopener">\n'
            f'    <img src="{src}" alt="{shot.get("alt", "")}">\n'
            f'  </a>\n'
            f'  <figcaption>{shot.get("caption", "")}</figcaption>\n'
            f'</figure>'
        )
    return "\n".join(blocks)


def plugin_sections(plugin):
    blocks = []
    for sec in plugin.get("sections", []):
        blocks.append(f'<section>\n  <h2>{sec["heading"]}</h2>\n  {sec["html"]}\n</section>')
    return "\n\n".join(blocks)


def validate(plugins):
    if not plugins:
        fail("plugins.json 里没有任何插件登记项")
    seen = set()
    for p in plugins:
        pid = p.get("id")
        if not pid:
            fail(f"插件登记项缺少 id：{p.get('name', p)}")
        if pid in seen:
            fail(f"插件 id 重复：{pid}")
        seen.add(pid)
        shots = p.get("screenshots", [])
        if not shots:
            fail(f"插件 {pid} 没有登记截图")
        for shot in shots:
            path = os.path.join(ASSETS_DIR, shot["file"])
            if not os.path.exists(path):
                fail(f"插件 {pid} 的截图缺失：{path}（请先运行 capture-screenshots.sh）")


def main():
    meta = load_json(os.path.join(ASSETS_DIR, "meta.json"), {})
    registry = load_json(os.path.join(TEMPLATE_DIR, "plugins.json"), None)
    if registry is None:
        fail(f"缺少插件登记表：{os.path.join(TEMPLATE_DIR, 'plugins.json')}")

    plugins = registry.get("plugins", [])
    validate(plugins)

    common = {
        "FORKPLUS_VERSION": str(meta.get("forkplus_version", "unknown")),
        "PLUGINS_VERSION": str(meta.get("plugins_version", "dev")),
        "GENERATED_AT": str(meta.get("generated_at", "")),
    }

    os.makedirs(os.path.join(PAGES_DIR, "plugins"), exist_ok=True)

    # ── 总览页 ────────────────────────────────────────────────────────────────
    index_tpl = open(os.path.join(TEMPLATE_DIR, "template-index.html"), encoding="utf-8").read()
    index_values = dict(common)
    index_values.update({
        "PLUGIN_COUNT": str(len(plugins)),
        "PLUGIN_CARDS": "\n".join(plugin_card(p) for p in plugins),
    })
    with open(os.path.join(PAGES_DIR, "index.html"), "w", encoding="utf-8") as f:
        f.write(render(index_tpl, index_values))

    # ── 每个插件一个子页 ──────────────────────────────────────────────────────
    plugin_tpl = open(os.path.join(TEMPLATE_DIR, "template-plugin.html"), encoding="utf-8").read()
    for p in plugins:
        values = dict(common)
        values.update({
            "PLUGIN_ID": p["id"],
            "PLUGIN_NAME": p.get("name", p["id"]),
            "PLUGIN_SUMMARY": p.get("summary", ""),
            "PLUGIN_VERSION": p.get("version", "-"),
            "PLUGIN_ASSEMBLY": p.get("assembly", "-"),
            "PLUGIN_SOURCE": p.get("source", "-"),
            "PLUGIN_EXTENSIONS": ext_codes(p.get("extensions", [])) or "<code>*</code>",
            "PLUGIN_SCREENSHOTS": plugin_screenshots(p),
            "PLUGIN_SECTIONS": plugin_sections(p),
        })
        out = os.path.join(PAGES_DIR, "plugins", f'{p["id"]}.html')
        with open(out, "w", encoding="utf-8") as f:
            f.write(render(plugin_tpl, values))
        print(f"  插件子页 -> {os.path.relpath(out, REPO_ROOT)}")

    shutil.copyfile(os.path.join(TEMPLATE_DIR, "style.css"), os.path.join(PAGES_DIR, "style.css"))

    # 生成空的 .nojekyll，避免 GitHub Pages 用 Jekyll 处理静态资源
    open(os.path.join(PAGES_DIR, ".nojekyll"), "w").close()

    print(f"pages 生成完成 -> {os.path.relpath(PAGES_DIR, REPO_ROOT)}/index.html")
    for k, v in common.items():
        print(f"  {k} = {v}")


if __name__ == "__main__":
    main()