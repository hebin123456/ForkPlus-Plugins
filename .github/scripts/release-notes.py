#!/usr/bin/env python3
"""生成 GitHub Release 版本说明。

按 conventional commit 前缀把上一个 tag 到当前 tag 之间的提交分组，输出 Markdown，
供 build.yml 的 Release 步骤通过 body_path 使用，保证每次发版都带版本说明
（不再只剩一行 Full Changelog）。

用法：release-notes.py <tag> [output-file]
环境变量：GITHUB_REPOSITORY（默认本仓库），用于拼 Full Changelog 链接。
"""
import collections
import os
import subprocess
import sys

# 分组顺序与标题：按 conventional commit 的 type 归类
ORDER = [
    ("feat", "新增"),
    ("fix", "修复"),
    ("perf", "性能"),
    ("refactor", "重构"),
    ("docs", "文档"),
    ("build", "构建"),
    ("ci", "CI"),
    ("test", "测试"),
    ("chore", "杂项"),
]
OTHER = "其它"
TITLES = {kind: title for kind, title in ORDER}


def git(*args):
    return subprocess.run(["git", *args], check=True, capture_output=True, text=True).stdout


def previous_tag(tag):
    try:
        out = git("describe", "--tags", "--abbrev=0", "--match", "v*", f"{tag}^").strip()
        return out or None
    except subprocess.CalledProcessError:
        return None


def kind_of(subject):
    """取 conventional commit 的 type（去掉 scope 与 ! ），非规范写法归为其它。"""
    head = subject.split(":", 1)[0]
    return head.split("(", 1)[0].split("!", 1)[0].strip()


def describe(subject):
    """去掉 "type(scope): " 前缀，只留描述文本；非规范写法原样返回。"""
    if ":" in subject:
        head, rest = subject.split(":", 1)
        stripped = head.strip()
        # 规范前缀形如 feat / fix(scope) / ci! ，不含空格
        if stripped and stripped == head and " " not in stripped and rest.startswith(" "):
            return rest.strip()
    return subject


def main():
    if len(sys.argv) < 2:
        sys.exit("用法: release-notes.py <tag> [output-file]")
    tag = sys.argv[1]
    out_path = sys.argv[2] if len(sys.argv) > 2 else None

    repo = os.environ.get("GITHUB_REPOSITORY", "hebin123456/ForkPlus-Plugins")
    prev = previous_tag(tag)
    if prev:
        revision = f"{prev}..{tag}"
        changelog = f"https://github.com/{repo}/compare/{prev}...{tag}"
    else:
        revision = tag
        changelog = f"https://github.com/{repo}/commits/{tag}"

    subjects = [line for line in git("log", "--no-merges", "--format=%s", revision).splitlines() if line.strip()]

    buckets = collections.OrderedDict((title, []) for _, title in ORDER)
    buckets[OTHER] = []
    for subject in subjects:
        buckets[TITLES.get(kind_of(subject), OTHER)].append(describe(subject))

    lines = []
    for _, title in ORDER:
        items = buckets[title]
        if not items:
            continue
        lines.append(f"## {title}")
        lines.append("")
        lines.extend(f"- {item}" for item in items)
        lines.append("")
    if buckets[OTHER]:
        lines.append(f"## {OTHER}")
        lines.append("")
        lines.extend(f"- {item}" for item in buckets[OTHER])
        lines.append("")
    if not subjects:
        lines.append("本版本无提交记录变更。")
        lines.append("")
    lines.append(f"**Full Changelog**: {changelog}")

    text = "\n".join(lines).rstrip() + "\n"
    if out_path:
        with open(out_path, "w", encoding="utf-8") as fh:
            fh.write(text)
    else:
        sys.stdout.write(text)


if __name__ == "__main__":
    main()
