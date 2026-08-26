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
#   # version 为必传参数,不传会直接报错:
#   bash .scripts/release_local.sh 0.0.3 --skip-version --from release/v0.0.3 --to release/v0.0.3 --push --force
#   bash .scripts/release_local.sh 0.0.4 --push --token github_pat_xxx   # 走 HTTPS
#   bash .scripts/release_local.sh --doctor --from main --to main        # 只体检,不改动
#
# 参数(对齐 pipeline inputs):
#   <version>          【必传】目标版本 v{a.b.c} 或 a.b.c;会写入 push 出去的
#                      package.json 的 version 字段。不传直接报错退出
#                      (不再从分支名或 package.json 推导,发布版本必须显式指定)。
#                      --doctor 只读体检模式除外(无需传 version)。
#   --doctor           只做只读环境体检(git/脚本/工作区/版本/认证/分支可达),
#                      不切分支、不改文件、不提交、不推送;通过 exit 0,否则 exit 1
#   --skip-version     跳过"新版本必须更大"校验(格式仍校验)
#   --strip-menu       发布前删除 Editor 下所有 `#if PICO_MCP_SHOW_MENU ... #endif`
#                      代码块(含指令与块内代码),让公开发布版不带手动验证用的
#                      Unity 菜单项。默认关闭(不传就完整保留菜单项代码)。
#   --tag <name>       推送时打的 tag 名,如 v0.0.4;不填不打 tag。同名 tag 已存在时
#                      本地用 -f 覆盖(幂等);推送时仅在加 --force 才覆盖远端同名 tag
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

usage() { sed -n '2,48p' "$0" | sed 's/^# \{0,1\}//'; }

# ---- 定位仓库根 ----
REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || {
  echo "ERROR: 当前不在 git 仓库内" >&2; exit 1; }
cd "$REPO_ROOT"

# ---- 记录起始分支/位置,结束时(无论成败)自动切回 ----
# 坑:不要用 `git symbolic-ref --short HEAD` 的输出作为切回参数——当存在同名 tag 等
# 引用歧义时,它会返回带前缀的 `heads/release/v0.0.4`;而 `git checkout heads/release/v0.0.4`
# 会被 git 当成 commit-ish 解析,切回时进入"分离头指针"(detached HEAD),
# 日志里也会打印难看的 `heads/` 前缀。这里改为:取完整 ref(refs/heads/xxx)剥掉前缀得到
# 干净分支名,切回统一用 `git switch`(只认分支、绝不分离);起始若本就是分离态则记 SHA。
ORIG_FULLREF="$(git symbolic-ref -q HEAD || true)"
if [ -n "$ORIG_FULLREF" ]; then
  ORIG_REF="${ORIG_FULLREF#refs/heads/}"   # 干净分支名,如 release/v0.0.4
  ORIG_IS_BRANCH=true
else
  ORIG_REF="$(git rev-parse HEAD)"          # 起始就是分离头指针,记 SHA
  ORIG_IS_BRANCH=false
fi

# 取"当前"引用的干净名称(分支名或 SHA),用于和 ORIG_REF 比较,避免 heads/ 前缀歧义
current_ref() {
  local f
  f="$(git symbolic-ref -q HEAD || true)"
  if [ -n "$f" ]; then echo "${f#refs/heads/}"; else git rev-parse HEAD; fi
}

# 切回起始位置:分支起点用 git switch(绝不分离),分离态起点用 checkout 到 SHA
switch_back() {
  if [ "$ORIG_IS_BRANCH" = true ]; then
    git switch -q "$ORIG_REF" 2>/dev/null || git checkout -q "$ORIG_REF" 2>/dev/null
  else
    git checkout -q "$ORIG_REF" 2>/dev/null
  fi
}

restore_branch() {
  local code=$?
  # 清理临时的 prepare_release.py / strip_show_menu.py 副本(在切分支前复制出来,避免 checkout 到不含 .codebase 的分支后丢失)
  [ -n "${PREPARE_TMP:-}" ] && rm -f "$PREPARE_TMP" 2>/dev/null || true
  [ -n "${STRIP_MENU_TMP:-}" ] && rm -f "$STRIP_MENU_TMP" 2>/dev/null || true
  if [ -n "${ORIG_REF:-}" ]; then
    local cur
    cur="$(current_ref)"
    if [ "$cur" != "$ORIG_REF" ]; then
      echo ""
      echo "[cleanup] 切回起始分支/位置: $ORIG_REF"
      # push 前的剥离用 `git rm -r --cached .codebase .scripts`,只把它们从索引移除,
      # 磁盘上会残留成"未跟踪文件"。直接切回仍跟踪这些目录的起始分支时,git 会
      # 报"未跟踪工作区文件会被覆盖"而中止,既切不回去、临时 prep 分支也删不掉。
      # 这些未跟踪内容与起始分支里跟踪的同名文件一致(仅索引被删,磁盘未改动),
      # 因此可安全地在切回前用 git clean 清掉,再由切回操作从起始分支重新恢复。
      if ! switch_back; then
        git clean -qfd -- .codebase .scripts 2>/dev/null || true
        switch_back \
          || git switch -q -f "$ORIG_REF" 2>/dev/null \
          || git checkout -q -f "$ORIG_REF" 2>/dev/null \
          || echo "[cleanup] 警告: 无法切回 $ORIG_REF,请手动执行 git switch $ORIG_REF" >&2
      fi
    fi
    # 只有确实回到了起始分支,才删除临时的 release/prep-* 分支(原脚本漏删,残留一堆 prep 分支)
    cur="$(current_ref)"
    if [ "$cur" = "$ORIG_REF" ] && [ -n "${PREP_BRANCH:-}" ]; then
      if git rev-parse --verify -q "$PREP_BRANCH" >/dev/null 2>&1; then
        git branch -D "$PREP_BRANCH" >/dev/null 2>&1 \
          && echo "[cleanup] 已删除临时分支 $PREP_BRANCH" \
          || echo "[cleanup] 警告: 无法删除临时分支 $PREP_BRANCH,请手动 git branch -D $PREP_BRANCH" >&2
      fi
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

# ---- 同理:切分支前把 strip_show_menu.py 也复制到临时文件 ----
# 仅在 --strip-menu 时才会用到;和 prepare_release.py 一样,--from 分支可能不含
# .codebase/,提前复制到仓库外,后续用 $STRIP_MENU_TMP 执行,不受切分支影响。
STRIP_MENU_SCRIPT="$REPO_ROOT/.codebase/scripts/strip_show_menu.py"
STRIP_MENU_TMP=""
if [ -f "$STRIP_MENU_SCRIPT" ]; then
  STRIP_MENU_TMP="$(mktemp -t strip_show_menu.XXXXXX.py)"
  cp "$STRIP_MENU_SCRIPT" "$STRIP_MENU_TMP"
fi

# ---- 默认参数 ----
VERSION=""
SKIP_VERSION=false
STRIP_MENU=false
TAG=""
FROM_BRANCH=""
TO_BRANCH="main"
DO_PUSH=false
NO_BRANCH=false
TOKEN_ARG=""
USE_SSH=false
FORCE=false
DOCTOR=false

# ---- 解析参数 ----
while [ $# -gt 0 ]; do
  case "$1" in
    --skip-version)        SKIP_VERSION=true; shift;;
    --strip-menu)          STRIP_MENU=true; shift;;
    --tag)                 TAG="$2"; shift 2;;
    --from|--from-branch)  FROM_BRANCH="$2"; shift 2;;
    --to|--to-branch)      TO_BRANCH="$2"; shift 2;;
    --push)                DO_PUSH=true; shift;;
    --no-branch)           NO_BRANCH=true; shift;;
    --token)               TOKEN_ARG="$2"; shift 2;;
    --ssh)                 USE_SSH=true; shift;;
    --force)               FORCE=true; shift;;
    --doctor)              DOCTOR=true; shift;;
    -h|--help)             usage; exit 0;;
    -*)                    echo "未知参数: $1" >&2; usage; exit 1;;
    *)                     if [ -z "$VERSION" ]; then VERSION="$1"; shift;
                           else echo "多余参数: $1" >&2; exit 1; fi;;
  esac
done

# 兜底:从当前工作区 package.json 读 version 字段(仅 --doctor 体检时用于展示当前版本)
read_pkg_version() {
  [ -f package.json ] || return 1
  python3 - <<'PY' 2>/dev/null
import json
try:
    with open("package.json", encoding="utf-8") as f:
        v = json.load(f).get("version", "")
    print(v) if v else exit(1)
except Exception:
    exit(1)
PY
}

# ---- --doctor:只读环境体检,不切分支/不改文件/不提交/不推送 ----
run_doctor() {
  local ok=0 warn=0 fail=0
  local mark
  pass() { echo "  [ok]   $1"; }
  wrn()  { echo "  [warn] $1"; warn=$((warn+1)); }
  err()  { echo "  [FAIL] $1" >&2; fail=$((fail+1)); }

  echo "==== release_local.sh doctor ===="

  # 1. git 仓库 & 仓库根
  if git rev-parse --show-toplevel >/dev/null 2>&1; then
    pass "git 仓库: $REPO_ROOT"
  else
    err "当前不在 git 仓库内"
  fi

  # 2. prepare_release.py 存在
  if [ -f "$PREPARE" ]; then
    pass "prepare_release.py 存在: $PREPARE"
  else
    err "找不到 $PREPARE(从不含 .codebase 的分支运行?请在含 .codebase 的分支执行)"
  fi

  # 3. 工作区是否干净(有未提交改动会挡住脚本内部 checkout)
  if [ -z "$(git status --porcelain 2>/dev/null)" ]; then
    pass "工作区干净"
  else
    wrn "工作区有未提交改动,脚本内部 git checkout 可能被阻挡(请先 commit/stash)"
  fi

  # 4. package.json 可解析 & version 合法
  if [ -f package.json ]; then
    local pv
    if pv="$(read_pkg_version)"; then
      if [[ "$pv" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        pass "package.json version 合法: $pv"
      else
        wrn "package.json version 非 a.b.c 格式: $pv"
      fi
    else
      err "package.json 无法解析或缺少 version 字段"
    fi
  else
    wrn "当前分支无 package.json"
  fi

  # 5. GitHub 认证方式
  local effective_token="${TOKEN_ARG:-${GITHUB_TOKEN:-}}"
  if [ "$USE_SSH" = false ] && [ -n "$effective_token" ]; then
    pass "GitHub 认证: 检测到 token,将走 HTTPS"
  else
    pass "GitHub 认证: 未检测到 token,将走 SSH(默认)"
  fi

  # 6. SSH 连通性(仅在会走 SSH 时检查)
  if [ "$USE_SSH" = true ] || [ -z "$effective_token" ]; then
    if command -v ssh >/dev/null 2>&1; then
      # GitHub 对成功认证返回 exit 1 + "successfully authenticated" 文案,不会给 shell
      local ssh_out
      ssh_out="$(ssh -T -o BatchMode=yes -o StrictHostKeyChecking=accept-new git@github.com 2>&1 || true)"
      if echo "$ssh_out" | grep -qi "successfully authenticated"; then
        pass "SSH 连通 github.com,key 认证通过"
      else
        wrn "SSH 未认证通过(推送可能失败):$(echo "$ssh_out" | head -1)"
      fi
    else
      wrn "未找到 ssh 命令,无法校验 SSH 连通性"
    fi
  fi

  # 7. --from / --to 分支可达(remote 存在性)
  local b
  if [ -n "$FROM_BRANCH" ]; then
    if git ls-remote --exit-code --heads origin "$FROM_BRANCH" >/dev/null 2>&1; then
      pass "--from 分支可达: origin/$FROM_BRANCH"
    else
      wrn "--from 分支在 origin 上不存在或不可达: $FROM_BRANCH"
    fi
  fi
  for b in ${TO_BRANCH}; do
    if git ls-remote --exit-code --heads origin "$b" >/dev/null 2>&1; then
      pass "--to 分支可达(origin): $b"
    else
      wrn "--to 分支在 origin 上暂不存在(push 时会新建): $b"
    fi
  done

  echo "==== doctor 结束: ${warn} warn, ${fail} fail ===="
  [ "$fail" -eq 0 ] && return 0 || return 1
}
if [ "$DOCTOR" = true ]; then
  run_doctor
  exit $?
fi

# ---- 需求:version 为必传参数 ----
# 不再从 --from/--to 的分支名推导,也不再兜底读 package.json;
# 未显式传入版本号一律报错退出,确保每次发布的版本号都是调用方明确指定的。
if [ -z "$VERSION" ]; then
  echo "ERROR: 未提供 version(必传)。请显式传入目标版本号,例如:" >&2
  echo "       bash .scripts/release_local.sh 0.0.4 --from release/v0.0.4 --to release/v0.0.4 --push" >&2
  usage; exit 1
fi

# ---- 需求 0:可选从 from-branch 切临时分支 ----
if [ "$NO_BRANCH" = false ]; then
  if [ -n "$FROM_BRANCH" ]; then
    # 先探测 origin 上是否真的存在该分支;不存在直接给出可执行的修复建议,
    # 避免 `git fetch` 抛出难懂的 "fatal: couldn't find remote ref release/v0.0.4"。
    # 典型场景:release/vX.Y.Z 已合入 main 且 MR 合并时勾选了"删除源分支",导致远端已无该分支。
    if ! git ls-remote --exit-code --heads origin "$FROM_BRANCH" >/dev/null 2>&1; then
      echo "ERROR: 远端 origin 上找不到分支 '$FROM_BRANCH'(couldn't find remote ref)。" >&2
      echo "       可能原因:该 release 分支已合入主干后被 MR 自动删除,或分支名拼写有误。" >&2
      echo "       可选处理:" >&2
      echo "         1) 用一个真实存在的分支作为 --from,例如: --from main" >&2
      echo "         2) 若确需该 release 分支,先基于主干重建:" >&2
      echo "              git checkout -b $FROM_BRANCH origin/main && git push origin $FROM_BRANCH" >&2
      echo "         3) 若本地已 checkout 到目标提交,去掉 --from 直接用当前工作区处理。" >&2
      echo "       现有远端分支:" >&2
      git ls-remote --heads origin 2>/dev/null | sed 's#.*refs/heads/#         - #' >&2
      exit 1
    fi
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

# ---- 需求 1:可选删除 Editor 下所有 `#if PICO_MCP_SHOW_MENU ... #endif` 代码块 ----
# 仅在显式传入 --strip-menu 时执行,让公开发布版不带手动验证用的 Unity 菜单项。
# 用 .codebase/scripts/strip_show_menu.py 做预处理器感知的成对删除(每个 SHOW_MENU
# 的 #if 只与自己对应的 #endif 配对,不会误吃到下一个块或内层 #endif),其它条件
# 编译块(ENABLE_PICO_XR_SDK / UNITY_2023_1_OR_NEWER 等)一律保留。
if [ "$STRIP_MENU" = true ]; then
  if [ -z "$STRIP_MENU_TMP" ] || [ ! -f "$STRIP_MENU_TMP" ]; then
    echo "ERROR: --strip-menu 需要 .codebase/scripts/strip_show_menu.py,但未找到" >&2
    exit 1
  fi
  # 收集 Editor 下的 .cs(含被 Unity 忽略的 SpatialMeshAssets~ 目录里的驱动脚本)。
  mapfile -t MENU_CS < <(find Editor -type f -name '*.cs' | sort)
  if [ "${#MENU_CS[@]}" -gt 0 ]; then
    echo "[strip-menu] 删除 PICO_MCP_SHOW_MENU 代码块,共 ${#MENU_CS[@]} 个文件"
    python3 "$STRIP_MENU_TMP" "${MENU_CS[@]}"
  else
    echo "[strip-menu] Editor 下未找到 .cs,跳过"
  fi
fi

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
  # 幂等打 tag:`git tag <name>` 在同名 tag 已存在时会以 "标签 '<name>' 已存在" 致命报错中止;
  # 这里用 -f 让本地 tag 指向当前 HEAD(覆盖旧值),重复发布同一版本也不会失败。
  echo "[tag] 在当前 HEAD 打 tag: ${TAG}(-f 覆盖同名本地 tag)"
  git tag -f "$TAG" >/dev/null
  # 推送 tag:远端若已存在同名 tag,普通 push 会因 non-fast-forward 被拒;
  # 与分支推送保持一致——仅当传了 --force 才用 --force 覆盖远端 tag,否则普通推送。
  if [ "$FORCE" = true ]; then
    echo "[tag] --force:强制推送 tag ${TAG} 覆盖远端"
    git push --force github "refs/tags/${TAG}"
  else
    echo "[tag] 推送 tag ${TAG}(远端已存在同名 tag 时,如需覆盖请加 --force)"
    git push github "refs/tags/${TAG}"
  fi
fi
echo "[done] 已推送到 GitHub。"
