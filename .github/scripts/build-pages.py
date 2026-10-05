#!/usr/bin/env python3
"""把截图与元数据注入 Pages 模板，生成可发布的静态站点。

输入：
  .github/pages/template.html   站点模板（含 {{PLACEHOLDER}} 占位符）
  .github/pages/style.css       样式表
  <pages>/assets/*.png          capture-screenshots.sh 产出的截图
  <pages>/assets/meta.json      ForkPlus / 插件版本与生成时间

输出：
  <pages>/index.html
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

REQUIRED_SHOTS = ["example-diff-full.png", "example-diff-detail.png"]


def fail(msg):
    print(f"ERROR: {msg}", file=sys.stderr)
    sys.exit(1)


def load_meta():
    path = os.path.join(ASSETS_DIR, "meta.json")
    if os.path.exists(path):
        return json.load(open(path, encoding="utf-8"))
    return {}


def main():
    missing = [s for s in REQUIRED_SHOTS if not os.path.exists(os.path.join(ASSETS_DIR, s))]
    if missing:
        fail(f"缺少截图：{', '.join(missing)}（请先运行 capture-screenshots.sh）")

    meta = load_meta()
    template = open(os.path.join(TEMPLATE_DIR, "template.html"), encoding="utf-8").read()

    values = {
        "FORKPLUS_VERSION": str(meta.get("forkplus_version", "unknown")),
        "PLUGINS_VERSION": str(meta.get("plugins_version", "dev")),
        "GENERATED_AT": str(meta.get("generated_at", "")),
    }

    def substitute(text):
        return re.sub(r"\{\{(\w+)\}\}", lambda m: values.get(m.group(1), m.group(0)), text)

    os.makedirs(PAGES_DIR, exist_ok=True)
    shutil.copyfile(os.path.join(TEMPLATE_DIR, "style.css"), os.path.join(PAGES_DIR, "style.css"))
    with open(os.path.join(PAGES_DIR, "index.html"), "w", encoding="utf-8") as f:
        f.write(substitute(template))

    # 生成空的 .nojekyll，避免 GitHub Pages 用 Jekyll 处理静态资源
    open(os.path.join(PAGES_DIR, ".nojekyll"), "w").close()

    print(f"pages 生成完成 -> {PAGES_DIR}/index.html")
    for k, v in values.items():
        print(f"  {k} = {v}")


if __name__ == "__main__":
    main()