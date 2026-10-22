#!/usr/bin/env python3
"""Regenerate docs/mcp-capabilities.md from the [McpServerTool]/[Description]
attributes in src/PtcXaeMcp.WinForms/Mcp/TwinCatMcpTools.*.cs (issue #157 item 4).

Why a script and not hand-maintained prose: the doc had drifted badly (it claimed
68 tools; the source has 144+ [McpServerTool] methods) because nothing forced it to
track the actual attributes. This reads the attributes directly, so it can never
list a tool that doesn't exist or the wrong parameter list — it can still be wrong
about *behavior* the description doesn't capture, which is a source-code problem,
not a generator problem.

Usage:
    python tools/gen_tool_reference.py                    # writes docs/mcp-capabilities.md
    python tools/gen_tool_reference.py --check             # exit 1 if the file would change
    python tools/gen_tool_reference.py --src <dir> --out <file>   # override paths

Parsing approach: this is a regex/bracket-counting scanner tuned to this codebase's
actual attribute style, not a general C# parser. It handles:
  - [McpServerTool(Name = "...")] followed by a tool-level [Description("..." + "...")]
    (string literals joined by +, across lines)
  - a `public <ReturnType> MethodName(...)` signature, with the parameter list matched
    by paren-depth counting (so nested parens in default values don't break it)
  - per-parameter [Description("...")] attributes, nullable types, default values
It does NOT evaluate expressions — a description built from anything other than
adjacent string literals (rare in this codebase) is emitted as-is/best-effort.
"""
from __future__ import annotations

import argparse
import pathlib
import re
import sys

REPO_ROOT = pathlib.Path(__file__).resolve().parent.parent
DEFAULT_SRC = REPO_ROOT / "src" / "PtcXaeMcp.WinForms" / "Mcp"
DEFAULT_OUT = REPO_ROOT / "docs" / "mcp-capabilities.md"

TOOL_ATTR_RE = re.compile(r'\[McpServerTool\(Name\s*=\s*"(?P<name>[a-zA-Z0-9_]+)"[^\]]*\)\]')
STRING_LIT_RE = re.compile(r'"((?:[^"\\]|\\.)*)"')


def unescape(s: str) -> str:
    return s.replace('\\"', '"').replace("\\n", "\n").replace("\\\\", "\\")


def concat_string_literals(segment: str) -> str:
    """Join adjacent "a" + "b" + "c" string literals in `segment` into one string."""
    parts = STRING_LIT_RE.findall(segment)
    return unescape("".join(parts))


def find_matching_paren(text: str, open_idx: int) -> int:
    """Index of the ')' matching the '(' at open_idx (which must be '(')."""
    depth = 0
    i = open_idx
    while i < len(text):
        c = text[i]
        if c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return i
        i += 1
    raise ValueError("Unbalanced parentheses")


def split_top_level(params_text: str) -> list[str]:
    """Split a parameter list on top-level commas only (not inside (), [], "")."""
    parts: list[str] = []
    depth = 0
    in_str = False
    buf = []
    i = 0
    while i < len(params_text):
        c = params_text[i]
        if in_str:
            buf.append(c)
            if c == "\\":
                i += 1
                if i < len(params_text):
                    buf.append(params_text[i])
            elif c == '"':
                in_str = False
        elif c == '"':
            in_str = True
            buf.append(c)
        elif c in "([{":
            depth += 1
            buf.append(c)
        elif c in ")]}":
            depth -= 1
            buf.append(c)
        elif c == "," and depth == 0:
            parts.append("".join(buf))
            buf = []
        else:
            buf.append(c)
        i += 1
    if "".join(buf).strip():
        parts.append("".join(buf))
    return [p.strip() for p in parts if p.strip()]


PARAM_DESC_RE = re.compile(r'\[Description\((?P<body>.*)\)\]\s*(?P<rest>.*)$', re.DOTALL)


def parse_param(raw: str) -> dict:
    """One top-level parameter chunk -> {description, type, name, default}."""
    raw = raw.strip()
    description = None
    m = PARAM_DESC_RE.match(raw)
    if m:
        description = concat_string_literals(m.group("body")).strip()
        raw = m.group("rest").strip()

    default = None
    if "=" in raw:
        # Split on the FIRST top-level '=' that isn't part of '==' / '=>' / '<=' / '>='.
        eq_idx = None
        depth = 0
        for i, c in enumerate(raw):
            if c in "([{":
                depth += 1
            elif c in ")]}":
                depth -= 1
            elif c == "=" and depth == 0:
                prev = raw[i - 1] if i > 0 else ""
                nxt = raw[i + 1] if i + 1 < len(raw) else ""
                if prev not in "=<>!" and nxt != "=":
                    eq_idx = i
                    break
        if eq_idx is not None:
            default = raw[eq_idx + 1:].strip()
            raw = raw[:eq_idx].strip()

    tokens = raw.split()
    if len(tokens) >= 2:
        ptype = " ".join(tokens[:-1])
        pname = tokens[-1]
    elif len(tokens) == 1:
        ptype, pname = "", tokens[0]
    else:
        ptype, pname = "", ""

    return {"description": description, "type": ptype, "name": pname, "default": default}


def parse_file(text: str) -> list[dict]:
    tools = []
    for m in TOOL_ATTR_RE.finditer(text):
        name = m.group("name")
        after = text[m.end():]

        # Optional tool-level [Description(...)] immediately following (allow blank
        # lines/whitespace, but not another attribute or the method itself first).
        desc_m = re.match(r'\s*\[Description\(', after)
        description = ""
        rest = after
        if desc_m:
            open_paren = after.index("(", desc_m.end() - 1)
            close_paren = find_matching_paren(after, open_paren)
            description = concat_string_literals(after[open_paren + 1:close_paren]).strip()
            rest = after[close_paren + 1:]
            rest = rest.lstrip()
            if rest.startswith("]"):
                rest = rest[1:]

        # Method signature: skip any further attribute lines (e.g. [return: ...]),
        # then `public <ReturnType> Name(`.
        sig_m = re.search(
            r'public\s+(?:async\s+)?(?P<ret>[\w<>\[\],\.\? ]+?)\s+(?P<method>\w+)\s*\(',
            rest,
        )
        if not sig_m:
            continue
        open_paren = rest.index("(", sig_m.end() - 1)
        close_paren = find_matching_paren(rest, open_paren)
        params_text = rest[open_paren + 1:close_paren]

        params = [parse_param(p) for p in split_top_level(params_text)]
        # CancellationToken and RequestContext<...> are bound automatically by
        # the MCP SDK (never exposed in the tool's input schema — verified in
        # McpToolCancellationTests, issue #151 item 1) and would otherwise show
        # up as a spurious, description-less parameter on every tool.
        params = [p for p in params if p["type"] not in ("CancellationToken",)
                  and "RequestContext" not in p["type"]]

        tools.append({
            "name": name,
            "method": sig_m.group("method"),
            "description": description,
            "params": params,
        })
    return tools


def render_markdown(tools_by_file: list[tuple[str, list[dict]]]) -> str:
    total = sum(len(tools) for _, tools in tools_by_file)
    lines = []
    lines.append("<!-- GENERATED FILE — do not hand-edit.")
    lines.append("     Regenerate with: python tools/gen_tool_reference.py")
    lines.append("     Source: [McpServerTool]/[Description] attributes in")
    lines.append("     src/PtcXaeMcp.WinForms/Mcp/TwinCatMcpTools.*.cs (issue #157 item 4). -->")
    lines.append("")
    lines.append("# MCP tool reference")
    lines.append("")
    lines.append(f"{total} tools, generated from the source attributes — one section per")
    lines.append("`TwinCatMcpTools.<Group>.cs` partial file, tools in source order within each.")
    lines.append("")
    lines.append("## Contents")
    lines.append("")
    for fname, tools in tools_by_file:
        if not tools:
            continue
        group = fname.replace("TwinCatMcpTools.", "").replace(".cs", "")
        anchor = group.lower()
        lines.append(f"- [{group}](#{anchor}) ({len(tools)})")
    lines.append("")

    for fname, tools in tools_by_file:
        if not tools:
            continue
        group = fname.replace("TwinCatMcpTools.", "").replace(".cs", "")
        lines.append(f"## {group}")
        lines.append("")
        for t in tools:
            lines.append(f"### `{t['name']}`")
            lines.append("")
            if t["description"]:
                lines.append(t["description"])
                lines.append("")
            if t["params"]:
                lines.append("| Parameter | Type | Default | Description |")
                lines.append("|---|---|---|---|")
                for p in t["params"]:
                    default = f"`{p['default']}`" if p["default"] else ""
                    desc = (p["description"] or "").replace("\n", " ").replace("|", "\\|")
                    ptype = p["type"].replace("|", "\\|")
                    lines.append(f"| `{p['name']}` | `{ptype}` | {default} | {desc} |")
                lines.append("")
            else:
                lines.append("_No parameters._")
                lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", type=pathlib.Path, default=DEFAULT_SRC)
    ap.add_argument("--out", type=pathlib.Path, default=DEFAULT_OUT)
    ap.add_argument("--check", action="store_true",
                     help="Don't write; exit 1 if the generated content differs from --out.")
    args = ap.parse_args()

    files = sorted(args.src.glob("TwinCatMcpTools.*.cs"))
    tools_by_file: list[tuple[str, list[dict]]] = []
    for f in files:
        text = f.read_text(encoding="utf-8")
        tools_by_file.append((f.name, parse_file(text)))

    total = sum(len(t) for _, t in tools_by_file)
    if total == 0:
        print(f"No [McpServerTool] methods found under {args.src}", file=sys.stderr)
        return 1

    rendered = render_markdown(tools_by_file)

    if args.check:
        current = args.out.read_text(encoding="utf-8") if args.out.exists() else ""
        if current != rendered:
            print(f"{args.out} is stale — run: python tools/gen_tool_reference.py", file=sys.stderr)
            return 1
        print(f"{args.out} is up to date ({total} tools).")
        return 0

    args.out.write_text(rendered, encoding="utf-8")
    print(f"Wrote {args.out} ({total} tools).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
