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
#   bash .scripts/release_local.sh 0.0.4 --push --to-branch "main dev"
#   bash .scripts/release_local.sh 0.0.4 --push --token github_pat_xxx
#   bash .scripts/release_local.sh 0.0.4 --push --ssh
#
# 参数(对齐 pipeline inputs):
#   <version>          目标版本 v{a.b.c} 或 a.b.c(必填)
#   --skip-version     跳过"新版本必须更大"校验(格式仍校验)
#   --tag <name>       推送时打的 tag 名,如 release/v0.0.3;不填不打 tag
#   --from-branch <b>  先从该分支拉取并 checkout(单个),默认用当前工作区
#   --to-branch <b...> push 目标分支,可空格分隔多个,默认 main
#   --push             真正推送到 GitHub(默认关闭 = 相当于 skip_push=true)
#   --no-branch        不切临时分支,原地在当前分支处理
#   --token <pat>      GitHub PAT;等价于设置环境变量 GITHUB_TOKEN(与 --ssh 二选一)
#   --ssh              用 SSH 推送(git@github.com:...),靠本机 SSH key 认证,免 token
#   --force            push 用 --force-with-lease 覆盖远端(解决 non-fast-forward)
#   -h|--help          显示帮助
#
# push 需要对 Pico-Developer/Unity-MCP-Extensions 有 push 权限,三种给法任选其一:
#   1) 环境变量:  GITHUB_TOKEN=github_pat_xxx bash .scripts/release_local.sh 0.0.4 --push
#   2) 参数:      bash .scripts/release_local.sh 0.0.4 --push --token github_pat_xxx
#   3) SSH(推荐): bash .scripts/release_local.sh 0.0.4 --push --ssh   # 免 token,靠本机 SSH key
# ==============================================================================
set -euo pipefail

GITHUB_HTTPS="https://github.com/Pico-Developer/Unity-MCP-Extensions.git"
GITHUB_SSH="git@github.com:Pico-Developer/Unity-MCP-Extensions.git"

usage() { sed -n '2,36p' "$0" | sed 's/^# \{0,1\}//'; }

# ---- 定位仓库根 ----
REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || {
  echo "ERROR: 当前不在 git 仓库内" >&2; exit 1; }
cd "$REPO_ROOT"

# ---- 记录起始分支/位置,结束时(无论成败)自动切回 ----
ORIG_REF="$(git symbolic-ref -q --short HEAD || git rev-parse HEAD)"
restore_branch() {
  local code=$?
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
    --skip-version) SKIP_VERSION=true; shift;;
    --tag)          TAG="$2"; shift 2;;
    --from-branch)  FROM_BRANCH="$2"; shift 2;;
    --to-branch)    TO_BRANCH="$2"; shift 2;;
    --push)         DO_PUSH=true; shift;;
    --no-branch)    NO_BRANCH=true; shift;;
    --token)        TOKEN_ARG="$2"; shift 2;;
    --ssh)          USE_SSH=true; shift;;
    --force)        FORCE=true; shift;;
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

# 确定远端 URL:--ssh 走 SSH(免 token);否则走 HTTPS+token
if [ "$USE_SSH" = true ]; then
  REMOTE_URL="$GITHUB_SSH"
  echo "[push] 使用 SSH 远端,靠本机 SSH key 认证(无需 token)"
else
  # token 两种给法:--token 参数 或 环境变量 GITHUB_TOKEN(参数优先)
  if [ -n "$TOKEN_ARG" ]; then
    GITHUB_TOKEN="$TOKEN_ARG"
  fi
  if [ -z "${GITHUB_TOKEN:-}" ]; then
    echo "ERROR: --push 需要凭证,三种给法任选其一:" >&2
    echo "  1) 环境变量:  GITHUB_TOKEN=github_pat_xxx bash .scripts/release_local.sh $VERSION --push" >&2
    echo "  2) 参数:      bash .scripts/release_local.sh $VERSION --push --token github_pat_xxx" >&2
    echo "  3) SSH(推荐): bash .scripts/release_local.sh $VERSION --push --ssh" >&2
    exit 1
  fi
  REMOTE_URL="https://x-access-token:${GITHUB_TOKEN}@github.com/Pico-Developer/Unity-MCP-Extensions.git"
fi

# push 前剥离内部目录:.codebase / .scripts 不进 GitHub(与 pipeline 一致)
git rm -r --cached .codebase .scripts 2>/dev/null || true
git commit -m "chore(release): strip internal dirs before GitHub push" || true

git remote remove github 2>/dev/null || true
git remote add github "$REMOTE_URL"
PUSH_OPTS=""
if [ "$FORCE" = true ]; then
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
