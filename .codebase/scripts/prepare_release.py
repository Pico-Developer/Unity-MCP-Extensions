#!/usr/bin/env python3
import os, re, sys, json, argparse

# ---------- 版权文本(正文,不含注释符) ----------
NOTICE_LINES = [
    "Copyright © 2015-2022 PICO Technology Co., Ltd.All rights reserved.",
    "",
    "NOTICE：All information contained herein is, and remains the property of",
    "PICO Technology Co., Ltd. The intellectual and technical concepts",
    "contained herein are proprietary to PICO Technology Co., Ltd. and may be",
    "covered by patents, patents in process, and are protected by trade secret or",
    "copyright law. Dissemination of this information or reproduction of this",
    "material is strictly forbidden unless prior written permission is obtained from",
    "PICO Technology Co., Ltd.",
]
MARKER = "Copyright © 2015-2022 PICO Technology"   # 幂等判断

C_FAMILY     = {".cs", ".cpp", ".cc", ".cxx", ".c", ".h", ".hpp", ".hh",
                ".js", ".jsx", ".ts", ".tsx", ".java", ".go", ".rs", ".kt", ".kts"}
HASH_FAMILY  = {".py", ".sh", ".rb"}
PLAIN_FAMILY = {".md", ".txt"}
# .json 故意排除:JSON 不支持注释
SKIP_EXT     = {".meta", ".asmdef", ".mat", ".shader", ".json"}


def build_header(ext: str) -> str:
    if ext in C_FAMILY:
        star = "*" * 79
        body = "\n".join(NOTICE_LINES)
        return f"/{star}\n{body}\n{star}/\n"
    if ext in HASH_FAMILY:
        return "\n".join(("# " + l).rstrip() for l in NOTICE_LINES) + "\n"
    if ext in PLAIN_FAMILY:
        return "\n".join(NOTICE_LINES) + "\n"
    return ""   # 其它扩展名不处理


def has_header(text: str) -> bool:
    return MARKER in text[:800]


def add_headers(root_dir="Editor"):
    for root, _, files in os.walk(root_dir):
        for name in files:
            ext = os.path.splitext(name)[1].lower()
            if ext in SKIP_EXT:
                continue
            header = build_header(ext)
            if not header:      # 非目标脚本类型,跳过
                continue
            path = os.path.join(root, name)
            with open(path, "r", encoding="utf-8") as f:
                content = f.read()
            if has_header(content):
                print(f"[header ok]    {path}")
                continue
            # 保留 shebang 行
            if content.startswith("#!"):
                first_nl = content.find("\n") + 1
                new = content[:first_nl] + header + "\n" + content[first_nl:]
            else:
                new = header + "\n" + content
            with open(path, "w", encoding="utf-8") as f:
                f.write(new)
            print(f"[header added] {path}")


# ---------- 版本处理 ----------
def normalize(v: str) -> str:
    v = v.strip()
    if v[:1] in ("v", "V"):
        v = v[1:]
    return v


def validate(v: str):
    if not re.fullmatch(r"\d+\.\d+\.\d+", v):
        sys.exit(f"[error] 版本格式非法,必须为 v{{a.b.c}}/a.b.c(数字): {v}")


def as_tuple(v: str):
    return tuple(int(x) for x in v.split("."))


def bump_version(new_v: str, skip_version: bool):
    new_v = normalize(new_v)
    validate(new_v)                      # 无论如何都校验格式
    with open("package.json", "r", encoding="utf-8") as f:
        pkg = json.load(f)
    old_v = normalize(str(pkg.get("version", "0.0.0")))
    if not skip_version:
        validate(old_v)
        if as_tuple(new_v) <= as_tuple(old_v):
            sys.exit(f"[error] 新版本 {new_v} 必须大于当前版本 {old_v}"
                     f"(如需跳过请加 --skip-version)")
    pkg["version"] = new_v
    with open("package.json", "w", encoding="utf-8") as f:
        json.dump(pkg, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"[version] package.json {old_v} -> {new_v}")
    return new_v


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("version", help="目标版本,v{a.b.c} 或 a.b.c")
    ap.add_argument("--skip-version", action="store_true",
                    help="跳过'新版本必须更大'校验(仍校验格式)")
    args = ap.parse_args()
    add_headers("Editor")
    final_v = bump_version(args.version, args.skip_version)
    # 把归一化后的版本写出,供 CI 后续步骤打 tag 用
    with open(".release_version", "w") as f:
        f.write(final_v)


if __name__ == "__main__":
    main()
