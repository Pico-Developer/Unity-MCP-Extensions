#!/usr/bin/env bash
# ==============================================================================
# release_local.sh — 本地复刻 release-to-github pipeline 流程
#
# 与 .codebase/pipelines/release.yml 使用同一个 prepare_release.py,
# 保证"补版权头 + 改版本 + 版本校验"逻辑与 CI 完全一致。
#
# 默认只做本地处理(切临时分支 + 补版权头 + 改版本 + 提交),不推送 GitHub。
# 只有显式加 --push 才会推送(且推送前会剥离 .codebase / .scripts)。
#
# 用法:
#   bash .scripts/release_local.sh 0.0.3
#   bash .scripts/release_local.sh v0.0.3 --skip-version
#   bash .scripts/release_local.sh 0.0.4 --push --tag release/v0.0.4
#   bash .scripts/release_local.sh 0.0.4 --push --to-branch "main dev"
#   bash .scripts/release_local.sh 0.0.4 --push --token github_pat_xxx
#
# 参数(对齐 pipeline inputs):
#   <version>          目标版本 v{a.b.c} 或 a.b.c(必填)
#   --skip-version     跳过"新版本必须更大"校验(格式仍校验)
#   --tag <name>       推送时打的 tag 名,如 release/v0.0.3;不填不打 tag
#   --from-branch <b>  先从该分支拉取并 checkout(单个),默认用当前工作区
#   --to-branch <b...> push 目标分支,可空格分隔多个,默认 main
#   --push             真正推送到 GitHub(默认关闭 = 相当于 skip_push=true)
#   --no-branch        不切临时分支,原地在当前分支处理
#   --token <pat>      GitHub PAT;等价于设置环境变量 GITHUB_TOKEN(二选一)
#   -h|--help          显示帮助
#
# push 需要 GitHub PAT(对 Pico-Developer/Unity-MCP-Extensions 有 push 权限),两种给法二选一:
#   1) 环境变量:  GITHUB_TOKEN=github_pat_xxx bash .scripts/release_local.sh 0.0.4 --push
#   2) 参数:      bash .scripts/release_local.sh 0.0.4 --push --token github_pat_xxx
# ==============================================================================
set -euo pipefail

GITHUB_REPO="https://github.com/Pico-Developer/Unity-MCP-Extensions.git"

usage() { sed -n '2,31p' "$0" | sed 's/^# \{0,1\}//'; }

# ---- 定位仓库根 ----
REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || {
  echo "ERROR: 当前不在 git 仓库内" >&2; exit 1; }
cd "$REPO_ROOT"

PREPARE="$REPO_ROOT/.codebase/scripts/prepare_release.py"
[ -f "$PREPARE" ] || { echo "ERROR: 找不到 $PREPARE" >&2; exit 1; }

# ---- 默认参数 ----
VERSION=""
SKIP_VERSION=false
TAG=""
FROM_BRANCH=""
TO_BRANCH="main"
DO_PUSH=false
NO_BRANCH=false
TOKEN_ARG=""

# ---- 解析参数 ----
while [ $# -gt 0 ]; do
  case "$1" in
    --skip-version) SKIP_VERSION=true; shift;;
    --tag)          TAG="$2"; shift 2;;
    --from-branch)  FROM_BRANCH="$2"; shift 2;;
    --to-branch)    TO_BRANCH="$2"; shift 2;;
    --push)         DO_PUSH=true; shift;;
    --no-branch)    NO_BRANCH=true; shift;;
    --token)        TOKEN_ARG="$2"; shift 2;;
    -h|--help)      usage; exit 0;;
    -*)             echo "未知参数: $1" >&2; usage; exit 1;;
    *)              if [ -z "$VERSION" ]; then VERSION="$1"; shift;
                    else echo "多余参数: $1" >&2; exit 1; fi;;
  esac
done

[ -n "$VERSION" ] || { echo "ERROR: 必须提供 version" >&2; usage; exit 1; }

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

# ---- 需求 1+2+校验:补版权头 + 改版本 ----
if [ "$SKIP_VERSION" = true ]; then
  python3 "$PREPARE" "$VERSION" --skip-version
else
  python3 "$PREPARE" "$VERSION"
fi
REL_VERSION="$(cat .release_version)"
rm -f .release_version

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

# token 两种给法:--token 参数 或 环境变量 GITHUB_TOKEN(参数优先)
if [ -n "$TOKEN_ARG" ]; then
  GITHUB_TOKEN="$TOKEN_ARG"
fi
if [ -z "${GITHUB_TOKEN:-}" ]; then
  echo "ERROR: --push 需要 GitHub PAT,两种给法二选一:" >&2
  echo "  1) 环境变量:  GITHUB_TOKEN=github_pat_xxx bash .scripts/release_local.sh $VERSION --push" >&2
  echo "  2) 参数:      bash .scripts/release_local.sh $VERSION --push --token github_pat_xxx" >&2
  exit 1
fi

# push 前剥离内部目录:.codebase / .scripts 不进 GitHub(与 pipeline 一致)
git rm -r --cached .codebase .scripts 2>/dev/null || true
git commit -m "chore(release): strip internal dirs before GitHub push" || true

git remote remove github 2>/dev/null || true
git remote add github "https://x-access-token:${GITHUB_TOKEN}@github.com/Pico-Developer/Unity-MCP-Extensions.git"
for b in ${TO_BRANCH}; do
  echo "pushing to github ${b}"
  git push github "HEAD:${b}"
done
if [ -n "$TAG" ]; then
  echo "pushing tag ${TAG}"
  git tag "$TAG"
  git push github "$TAG"
fi
echo "[done] 已推送到 GitHub。"
