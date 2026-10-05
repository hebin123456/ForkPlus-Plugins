#!/usr/bin/env python3
"""第三方许可声明生成器（单一事实来源：各插件的 third-party.json + licenses/ 全文）。

用法：
  collect-third-party-notices.py plugin <插件工程目录> <输出文件>
      生成某个插件随包分发的合并声明 <Assembly>.THIRD-PARTY-NOTICES.txt。
      该插件没有 third-party.json 时不产出任何文件（退出码 0）。

  collect-third-party-notices.py repo <输出文件>
      汇总仓库内所有插件的三方组件，生成根 THIRD-PARTY-NOTICES.md 总览。

  collect-third-party-notices.py repo <输出文件> --check
      仅校验已提交的根清单与生成结果一致（不一致则退出码 1），用于 CI 防漂移。

约定：
  - <插件工程目录>/third-party.json 声明该插件分发的组件；
  - licenseFile 相对仓库根，指向 licenses/ 下的许可全文；
  - 新增依赖只需在 third-party.json 追加条目并向 licenses/ 落全文，脚本无需改动。
"""
import glob
import json
import os
import sys

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
HEADER_RULE = "=" * 72


def fail(msg):
    print(f"ERROR: {msg}", file=sys.stderr)
    sys.exit(1)


def read_text(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def read_license_text(path):
    """许可全文原样保留：个别三方文件（如 PDFium 汇编里的 ICU 段）并非 UTF-8，
    退化为 latin-1 读取，避免因编码问题丢失或损坏许可内容。"""
    with open(path, "rb") as f:
        raw = f.read()
    for enc in ("utf-8", "latin-1"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            continue
    return raw.decode("utf-8", errors="replace")


def load_manifest(projdir):
    """读取插件登记表；不存在返回 None。"""
    path = os.path.join(projdir, "third-party.json")
    if not os.path.exists(path):
        return None
    try:
        data = json.loads(read_text(path))
    except json.JSONDecodeError as e:
        fail(f"{path} 不是合法 JSON：{e}")
    components = data.get("components") or []
    for c in components:
        for key in ("name", "license", "licenseFile"):
            if not c.get(key):
                fail(f"{path} 的条目缺少字段 {key}：{c}")
        lic = os.path.join(REPO_ROOT, c["licenseFile"])
        if not os.path.exists(lic):
            fail(f"{path} 引用的许可全文不存在：{c['licenseFile']}")
    return components


def component_lines(c, index):
    """声明头部：一个组件的元信息（不含全文）。"""
    lines = [f"  {index}) {c['name']}" + (f" {c['version']}" if c.get("version") else "")]
    lines.append(f"     许可 / License  : {c['license']}")
    if c.get("copyright"):
        lines.append(f"     版权 / Copyright: {c['copyright']}")
    if c.get("homepage"):
        lines.append(f"     主页 / Homepage : {c['homepage']}")
    return lines


def build_plugin_notice(assembly, components):
    out = [
        assembly,
        "第三方许可声明 / Third-Party Notices",
        HEADER_RULE,
        "",
        "本插件随包分发了下列第三方组件，完整许可全文附后。",
        "This plugin redistributes the third-party components listed below; full license texts follow.",
        "",
    ]
    for i, c in enumerate(components, 1):
        out.extend(component_lines(c, i))
        out.append("")
    out += [HEADER_RULE, "许可全文 / License Texts", HEADER_RULE, ""]
    for i, c in enumerate(components, 1):
        title = f"{i}) {c['name']}" + (f" {c['version']}" if c.get("version") else "")
        out.append(f"-------- {title} — {c['license']} --------")
        out.append("")
        out.append(read_license_text(os.path.join(REPO_ROOT, c["licenseFile"])).rstrip("\n"))
        out.append("")
    return "\n".join(out).rstrip("\n") + "\n"


def collect_repo_components():
    """扫描 plugins/*/third-party.json，按 (name, version, licenseFile) 去重并归并插件。"""
    grouped = {}
    for manifest in sorted(glob.glob(os.path.join(REPO_ROOT, "plugins", "*", "third-party.json"))):
        projdir = os.path.dirname(manifest)
        assembly = os.path.basename(projdir)
        for c in load_manifest(projdir) or []:
            key = (c["name"], c.get("version", ""), c["licenseFile"])
            entry = grouped.setdefault(key, {"c": c, "plugins": []})
            if assembly not in entry["plugins"]:
                entry["plugins"].append(assembly)
    return grouped


def build_repo_notice(grouped):
    out = [
        "# 第三方许可 / Third-Party Notices",
        "",
        "本仓库自身以 MIT 许可发布（见 [LICENSE](LICENSE)）。插件在运行期还会随包分发下列第三方组件，",
        "这些组件以各自的许可条款为准，完整许可全文集中存放在 [`licenses/`](licenses/)。",
        "",
        "## 组件总览",
        "",
        "| 组件 | 版本 | 许可 | 版权 | 分发的插件 | 全文 |",
        "| --- | --- | --- | --- | --- | --- |",
    ]
    for (name, version, license_file), entry in sorted(grouped.items()):
        c = entry["c"]
        plugins = "、".join(f"[{p}](plugins/{p})" for p in entry["plugins"])
        out.append(
            f"| {name} | {version or '-'} | {c['license']} | {c.get('copyright', '-')} | "
            f"{plugins} | [{os.path.basename(license_file)}]({license_file}) |"
        )
    out += [
        "",
        "## 分发形态",
        "",
        "每个插件包内附带一份合并声明 `<Assembly>.THIRD-PARTY-NOTICES.txt`，由",
        "[.github/scripts/collect-third-party-notices.py](.github/scripts/collect-third-party-notices.py)",
        "依据各插件的 `third-party.json` 与 `licenses/` 下的全文自动生成。",
        "",
        "新增依赖：把许可全文落到 `licenses/<组件>/`，并在对应插件的 `third-party.json` 追加条目——脚本与 CI 均无需改动。",
        "",
        "> 本文件由脚本生成，请勿手改；修改请编辑 `third-party.json` 后执行",
        "> `python3 .github/scripts/collect-third-party-notices.py repo THIRD-PARTY-NOTICES.md`。",
        "",
    ]
    return "\n".join(out)


def main():
    args = sys.argv[1:]
    if not args:
        fail(__doc__.strip().splitlines()[0])
    mode = args[0]

    if mode == "plugin":
        if len(args) != 3:
            fail("用法: collect-third-party-notices.py plugin <插件工程目录> <输出文件>")
        projdir, outfile = args[1], args[2]
        assembly = os.path.basename(os.path.normpath(projdir))
        csproj = glob.glob(os.path.join(projdir, "*.csproj"))
        if csproj:
            import re
            m = re.search(r"<AssemblyName>([^<]+)</AssemblyName>", read_text(csproj[0]))
            if m:
                assembly = m.group(1)
        components = load_manifest(projdir)
        if not components:
            print(f"  no third-party.json: {assembly}（跳过许可声明）", file=sys.stderr)
            return
        with open(outfile, "w", encoding="utf-8") as f:
            f.write(build_plugin_notice(assembly, components))
        print(f"  notices: {os.path.basename(outfile)}（{len(components)} 个组件）", file=sys.stderr)
        return

    if mode == "repo":
        if len(args) < 2:
            fail("用法: collect-third-party-notices.py repo <输出文件> [--check]")
        outfile, check = args[1], "--check" in args[2:]
        rendered = build_repo_notice(collect_repo_components())
        if check:
            current = read_text(outfile) if os.path.exists(outfile) else ""
            if current != rendered:
                fail(f"{outfile} 与生成结果不一致，请重新生成本文件。")
            print(f"  第三方许可清单已是最新：{os.path.relpath(outfile, REPO_ROOT)}", file=sys.stderr)
            return
        with open(outfile, "w", encoding="utf-8") as f:
            f.write(rendered)
        print(f"  第三方许可清单 -> {os.path.relpath(outfile, REPO_ROOT)}", file=sys.stderr)
        return

    fail(f"未知模式：{mode}")


if __name__ == "__main__":
    main()
