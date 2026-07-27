#!/usr/bin/env bash
# ==============================================================================
# release_local.sh — 本地复刻 release-to-github pipeline 流程
#
# 与 .codebase/pipelines/release.yml 使用同一个 prepare_release.py,
# 保证"补版权头 + 改版本 + 版本校验"逻辑与 CI 完全一致。
#
# 默认只做本地处理(切临时分支 + 补版权头 + 改版本 + 提交),不推送 GitHub。
# 只有显式加 --push 才会推送(且推送前会剥离 .codebase / .scripts)。
# 无论成功/失败/中断,脚本结束时都会自动切回起始分支。
#
# 用法:
#   bash .scripts/release_local.sh 0.0.3
#   bash .scripts/release_local.sh v0.0.3 --skip-version
#   bash .scripts/release_local.sh 0.0.4 --push --tag release/v0.0.4
#   bash .scripts/release_local.sh 0.0.4 --push --to "main dev"
#   bash .scripts/release_local.sh 0.0.4 --push --from release/v0.0.4 --to release/v0.0.4
#   # version 省略时会从 --from / --to 的 release/vX.Y.Z 分支名推导:
#   bash .scripts/release_local.sh --skip-version --from release/v0.0.3 --to release/v0.0.3 --push --force
#   bash .scripts/release_local.sh 0.0.4 --push --token github_pat_xxx   # 走 HTTPS
#
# 参数(对齐 pipeline inputs):
#   [version]          目标版本 v{a.b.c} 或 a.b.c(可选);会写入 push 出去的
#                      package.json 的 version 字段。省略时从 --from / --to 的
#                      release/vX.Y.Z 分支名推导(推不出则报错要求显式传入)。
#   --skip-version     跳过"新版本必须更大"校验(格式仍校验)
#   --tag <name>       推送时打的 tag 名,如 release/v0.0.3;不填不打 tag
#   --from <b>         先从该分支拉取并 checkout(单个),默认用当前工作区(别名 --from-branch)
#   --to <b...>        push 目标分支,可空格分隔多个,默认 main(别名 --to-branch)
#   --push             真正推送到 GitHub(默认关闭 = 相当于 skip_push=true)
#   --no-branch        不切临时分支,原地在当前分支处理
#   --token <pat>      GitHub PAT;传了就走 HTTPS+token(等价于设置环境变量 GITHUB_TOKEN)
#   --ssh              强制用 SSH 推送(默认即 SSH,此参数保留兼容)
#   --force            push 用 --force-with-lease 覆盖远端(解决 non-fast-forward)
#   -h|--help          显示帮助
#
# 推送认证(默认 SSH,不传 token 就走 SSH):
#   1) SSH(默认): bash .scripts/release_local.sh 0.0.4 --push          # 免 token,靠本机 SSH key
#   2) 环境变量:  GITHUB_TOKEN=github_pat_xxx bash .scripts/release_local.sh 0.0.4 --push  # 走 HTTPS
#   3) 参数:      bash .scripts/release_local.sh 0.0.4 --push --token github_pat_xxx        # 走 HTTPS
# ==============================================================================
set -euo pipefail

GITHUB_HTTPS="https://github.com/Pico-Developer/Unity-MCP-Extensions.git"
GITHUB_SSH="git@github.com:Pico-Developer/Unity-MCP-Extensions.git"

usage() { sed -n '2,45p' "$0" | sed 's/^# \{0,1\}//'; }

# ---- 定位仓库根 ----
REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || {
  echo "ERROR: 当前不在 git 仓库内" >&2; exit 1; }
cd "$REPO_ROOT"

# ---- 记录起始分支/位置,结束时(无论成败)自动切回 ----
ORIG_REF="$(git symbolic-ref -q --short HEAD || git rev-parse HEAD)"
restore_branch() {
  local code=$?
  # 清理临时的 prepare_release.py 副本(在切分支前复制出来,避免 checkout 到不含 .codebase 的分支后丢失)
  [ -n "${PREPARE_TMP:-}" ] && rm -f "$PREPARE_TMP" 2>/dev/null || true
  if [ -n "${ORIG_REF:-}" ]; then
    local cur
    cur="$(git symbolic-ref -q --short HEAD || git rev-parse HEAD)"
    if [ "$cur" != "$ORIG_REF" ]; then
      echo ""
      echo "[cleanup] 切回起始分支/位置: $ORIG_REF"
      git checkout -q "$ORIG_REF" 2>/dev/null \
        || echo "[cleanup] 警告: 无法切回 $ORIG_REF(工作区可能有未提交改动),请手动处理" >&2
    fi
  fi
  exit $code
}
trap restore_branch EXIT

PREPARE="$REPO_ROOT/.codebase/scripts/prepare_release.py"
[ -f "$PREPARE" ] || { echo "ERROR: 找不到 $PREPARE" >&2; exit 1; }

# ---- 关键:切分支前把 prepare_release.py 复制到临时文件 ----
# --from release/vX.Y.Z 等分支可能不包含 .codebase/ 目录,checkout 过去后 $PREPARE
# 会随工作区消失,导致后面 python3 "$PREPARE" 报 [Errno 2] No such file or directory。
# 这里先复制到仓库外的临时文件,后续统一用 $PREPARE_TMP 执行,不受切分支影响。
PREPARE_TMP="$(mktemp -t prepare_release.XXXXXX.py)"
cp "$PREPARE" "$PREPARE_TMP"

# ---- 默认参数 ----
VERSION=""
SKIP_VERSION=false
TAG=""
FROM_BRANCH=""
TO_BRANCH="main"
DO_PUSH=false
NO_BRANCH=false
TOKEN_ARG=""
USE_SSH=false
FORCE=false

# ---- 解析参数 ----
while [ $# -gt 0 ]; do
  case "$1" in
    --skip-version)        SKIP_VERSION=true; shift;;
    --tag)                 TAG="$2"; shift 2;;
    --from|--from-branch)  FROM_BRANCH="$2"; shift 2;;
    --to|--to-branch)      TO_BRANCH="$2"; shift 2;;
    --push)                DO_PUSH=true; shift;;
    --no-branch)           NO_BRANCH=true; shift;;
    --token)               TOKEN_ARG="$2"; shift 2;;
    --ssh)                 USE_SSH=true; shift;;
    --force)               FORCE=true; shift;;
    -h|--help)             usage; exit 0;;
    -*)                    echo "未知参数: $1" >&2; usage; exit 1;;
    *)                     if [ -z "$VERSION" ]; then VERSION="$1"; shift;
                           else echo "多余参数: $1" >&2; exit 1; fi;;
  esac
done

# ---- 需求 2:version 省略时,从 --from / --to 的 release/vX.Y.Z 分支名推导 ----
# 命令里已经给了 --from release/v0.0.3 --to release/v0.0.3,再重复写一遍 0.0.3 是冗余的。
# 优先看 --from,再看 --to(可能空格分隔多个,逐个匹配),取第一个 release/vX.Y.Z 命中。
derive_version_from_branch() {
  local b
  for b in $FROM_BRANCH $TO_BRANCH; do
    if [[ "$b" =~ ^release/[vV]?([0-9]+\.[0-9]+\.[0-9]+)$ ]]; then
      echo "${BASH_REMATCH[1]}"; return 0
    fi
  done
  return 1
}
if [ -z "$VERSION" ]; then
  if VERSION="$(derive_version_from_branch)"; then
    echo "[version] 未显式传 version,从分支名推导得到: $VERSION"
  else
    echo "ERROR: 未提供 version,且无法从 --from/--to 的 release/vX.Y.Z 分支名推导。" >&2
    echo "       请显式传入版本号,或把 --from/--to 指向 release/vX.Y.Z 形式的分支。" >&2
    usage; exit 1
  fi
fi

# ---- 需求 0:可选从 from-branch 切临时分支 ----
if [ "$NO_BRANCH" = false ]; then
  if [ -n "$FROM_BRANCH" ]; then
    git fetch origin "$FROM_BRANCH"
    git checkout -B "$FROM_BRANCH" "origin/$FROM_BRANCH"
  fi
  PREP_BRANCH="release/prep-$(date +%s)"
  git checkout -b "$PREP_BRANCH"
  echo "[branch] 已切到临时分支 $PREP_BRANCH"
else
  echo "[branch] --no-branch:原地在当前分支处理"
fi

# ---- 需求 1+3+校验:补版权头 + 改版本 ----
if [ "$SKIP_VERSION" = true ]; then
  python3 "$PREPARE_TMP" "$VERSION" --skip-version
else
  python3 "$PREPARE_TMP" "$VERSION"
fi
REL_VERSION="$(cat .release_version)"
rm -f .release_version

# ---- 需求 3:确保 push 出去的 package.json version = 目标版本(显式强制写入) ----
# prepare_release.py 已写入,这里再做一次强制写入 + 校验,保证最终一定生效。
python3 - "$REL_VERSION" <<'PY'
import json, sys
ver = sys.argv[1]
with open("package.json", "r", encoding="utf-8") as f:
    pkg = json.load(f)
if pkg.get("version") != ver:
    pkg["version"] = ver
    with open("package.json", "w", encoding="utf-8") as f:
        json.dump(pkg, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"[version] package.json 强制写入 version = {ver}")
else:
    print(f"[version] package.json version 已是 {ver}")
PY

git add -A
git commit -m "chore(release): headers & bump to ${REL_VERSION}" || echo "nothing to commit"

echo "==== 版本: ${REL_VERSION} ===="
git --no-pager show --stat HEAD | head -60

# ---- 需求 3/4:可选推送 GitHub(默认关闭)----
if [ "$DO_PUSH" = false ]; then
  echo ""
  echo "[skip push] 默认只做本地处理。确认无误后加 --push 才会推送到 GitHub。"
  exit 0
fi

# ---- 需求 4:确定远端 URL ----
# 默认走 SSH;只有显式给了 token(--token 参数 或 GITHUB_TOKEN 环境变量)才走 HTTPS。
if [ -n "$TOKEN_ARG" ]; then
  GITHUB_TOKEN="$TOKEN_ARG"
fi
if [ "$USE_SSH" = false ] && [ -n "${GITHUB_TOKEN:-}" ]; then
  REMOTE_URL="https://x-access-token:${GITHUB_TOKEN}@github.com/Pico-Developer/Unity-MCP-Extensions.git"
  echo "[push] 检测到 token,使用 HTTPS 远端"
else
  REMOTE_URL="$GITHUB_SSH"
  echo "[push] 默认使用 SSH 远端,靠本机 SSH key 认证(无需 token)"
fi

# ---- 需求 1:push 前剥离内部目录 .codebase / .scripts(与 pipeline 一致)----
# 注意:git rm 传多个 pathspec 时是"原子"的——只要其中一个在索引里不存在,整条命令
# 会失败且什么都不删,配合 "|| true" 被静默吞掉,曾导致 .codebase 泄漏到 GitHub。
# 这里改成逐个独立剥离,并用 --ignore-unmatch 保证互不影响。
STRIPPED=false
for d in .codebase .scripts; do
  if git rm -r --cached --ignore-unmatch "$d" >/dev/null 2>&1; then
    if [ -n "$(git status --porcelain -- "$d")" ]; then
      echo "[strip] 已从索引剥离 $d"; STRIPPED=true
    fi
  fi
done
if [ "$STRIPPED" = true ]; then
  git commit -m "chore(release): strip internal dirs before GitHub push" || true
fi
# 断言:确认 .codebase / .scripts 确实不在待推送内容里,否则中止,避免内部目录泄露到 GitHub
for d in .codebase .scripts; do
  if git ls-files --error-unmatch "$d" >/dev/null 2>&1; then
    echo "ERROR: $d 仍在待推送内容中,已中止 push 以防内部目录泄露到 GitHub" >&2
    exit 1
  fi
done

git remote remove github 2>/dev/null || true
git remote add github "$REMOTE_URL"
PUSH_OPTS=""
if [ "$FORCE" = true ]; then
  # --force-with-lease 需要远端跟踪引用作对比基准;刚 add 的 remote 未 fetch 会报 stale info
  echo "[push] --force:先 fetch github 以建立对比基准"
  git fetch github 2>/dev/null || true
  PUSH_OPTS="--force-with-lease"
  echo "[push] --force:使用 --force-with-lease 安全覆盖远端"
fi
for b in ${TO_BRANCH}; do
  echo "pushing to github ${b}"
  git push $PUSH_OPTS github "HEAD:${b}"
done
if [ -n "$TAG" ]; then
  echo "pushing tag ${TAG}"
  git tag "$TAG"
  git push github "$TAG"
fi
echo "[done] 已推送到 GitHub。"
