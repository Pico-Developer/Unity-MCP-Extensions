#!/usr/bin/env python3
# strip_show_menu.py — remove every `#if PICO_MCP_SHOW_MENU ... #endif` block
# (the directives AND their body) from the given C# source files.
#
# Used by the release pipeline / release_local.sh --strip-menu so the version
# published to public GitHub ships WITHOUT the manual-validation Unity menu items
# (they are gated behind the PICO_MCP_SHOW_MENU scripting-define, off by default).
#
# Preprocessor-aware: each SHOW_MENU `#if` is matched to its OWN corresponding
# `#endif` by depth counting, so
#   * a nested `#if`/`#endif` inside a SHOW_MENU block never mis-pairs the opener
#     with an inner `#endif`, and
#   * the first SHOW_MENU `#if` is NEVER paired with a LATER (second) SHOW_MENU
#     block's `#endif`.
# Non-SHOW_MENU conditional blocks (ENABLE_PICO_XR_SDK, UNITY_2023_1_OR_NEWER, ...)
# are left completely untouched.
import re, sys

GUARD = "PICO_MCP_SHOW_MENU"

# `#if PICO_MCP_SHOW_MENU` as a standalone condition (allow spaces after '#').
RE_SHOW_MENU_IF = re.compile(r'^\s*#\s*if\s+' + re.escape(GUARD) + r'\s*$')
# Any opening conditional directive (raises nesting depth).
RE_IF_ANY       = re.compile(r'^\s*#\s*if(?:def|ndef)?\b')
# Closing directive (lowers nesting depth).
RE_ENDIF        = re.compile(r'^\s*#\s*endif\b')


def strip_text(text):
    lines = text.splitlines(keepends=True)
    out = []
    i, n = 0, len(lines)
    removed = 0
    while i < n:
        if RE_SHOW_MENU_IF.match(lines[i]):
            # Found a SHOW_MENU opener; walk forward tracking depth so we close on
            # the matching #endif, not an inner one (and not a later block's).
            depth = 1
            j = i + 1
            while j < n and depth > 0:
                if RE_IF_ANY.match(lines[j]):
                    depth += 1
                elif RE_ENDIF.match(lines[j]):
                    depth -= 1
                    if depth == 0:
                        break
                j += 1
            if depth != 0:
                # Unbalanced: no matching #endif. Refuse rather than corrupt code.
                raise SystemExit(
                    "[strip-menu] unbalanced '#if %s' (no matching #endif)" % GUARD)
            # Drop lines[i .. j] inclusive (guard open .. matching endif).
            i = j + 1
            removed += 1
            continue
        out.append(lines[i])
        i += 1
    return "".join(out), removed


def strip_file(path):
    with open(path, "r", encoding="utf-8") as f:
        text = f.read()
    new, removed = strip_text(text)
    if removed:
        with open(path, "w", encoding="utf-8") as f:
            f.write(new)
    return removed


def main():
    if len(sys.argv) < 2:
        sys.exit("usage: strip_show_menu.py <file.cs> [<file.cs> ...]")
    total = 0
    for path in sys.argv[1:]:
        removed = strip_file(path)
        total += removed
        print(f"[strip-menu] {path}: removed {removed} block(s)")
    print(f"[strip-menu] total removed {total} block(s)")


if __name__ == "__main__":
    main()
