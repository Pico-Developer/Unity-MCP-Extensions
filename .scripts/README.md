# `.scripts/` 目录说明

本目录存放**仓库内部**的本地运维/发布脚本。与 `.codebase/` 一样,这些内容属于内部工具链,
**不会**随 GitHub 发布一起推送出去 —— `release_local.sh` 在 `--push` 前会主动把 `.codebase/`
和 `.scripts/` 从待推送内容中剥离(见下文"发布前剥离内部目录")。

| 脚本 | 作用 | 一句话说明 |
| --- | --- | --- |
| [`release_local.sh`](./release_local.sh) | 本地复刻 release-to-github 发布流程 | 切临时分支 → 补版权头 → 改版本 → 提交 →(可选)剥离内部目录后推送 GitHub |

---

## `release_local.sh`

### 功能

在本地一比一复刻 `.codebase/pipelines/release.yml` 的发布流程。它与 CI 复用**同一个**
`.codebase/scripts/prepare_release.py`,因此"补版权头 + 改版本号 + 版本校验"的逻辑与线上
CI 完全一致,避免"本地能过、CI 不过"的偏差。

默认行为(**不加 `--push`**)只做本地处理,**不会**推送到 GitHub:

1. (可选)从 `--from` 指定的分支拉取并切到临时分支;
2. 调用 `prepare_release.py` 补齐源码版权头、把版本号写入 `package.json`;
3. 二次强制写入并校验 `package.json` 的 `version` 字段;
4. 生成一次发布提交 `chore(release): headers & bump to <version>`。

只有显式加 `--push` 才会真正推送,并且**推送前**会把 `.codebase/`、`.scripts/`
从索引中剥离(与 pipeline 一致,防止内部目录泄露到公开的 GitHub 仓库)。

无论脚本成功、失败还是中途被打断,结束时都会通过 `trap ... EXIT` **自动切回起始分支/提交**,
并清理临时复制出的 `prepare_release.py`。

### 用法

```bash
# 最简:只做本地处理(补版权头 + 改版本 + 提交),不推送
bash .scripts/release_local.sh 0.0.4

# 跳过"新版本必须更大"的校验(格式仍会校验)
bash .scripts/release_local.sh v0.0.4 --skip-version

# 从 release/v0.0.4 拉取,处理后推回 GitHub 的 release/v0.0.4,并强制覆盖
bash .scripts/release_local.sh 0.0.4 --skip-version \
  --from release/v0.0.4 --to release/v0.0.4 --push --force

# 推送并打 tag
bash .scripts/release_local.sh 0.0.4 --push --tag release/v0.0.4

# 一次推多个目标分支(空格分隔)
bash .scripts/release_local.sh 0.0.4 --push --to "main dev"

# 只做只读环境体检,不改动任何东西(无需传 version)
bash .scripts/release_local.sh --doctor --from release/v0.0.4 --to release/v0.0.4
```

### 参数

| 参数 | 是否必传 | 说明 |
| --- | --- | --- |
| `<version>` | **是**(`--doctor` 模式除外) | 目标版本,`v{a.b.c}` 或 `a.b.c`,会写入推送出去的 `package.json` 的 `version`。**不再**从分支名或 `package.json` 推导,不传直接报错退出。 |
| `--doctor` | 否 | 只读环境体检(git 仓库 / 脚本 / 工作区 / 版本 / GitHub 认证 / `--from`&`--to` 分支可达性),不切分支、不改文件、不提交、不推送。全部通过 `exit 0`,否则 `exit 1`。 |
| `--skip-version` | 否 | 跳过"新版本号必须比旧的大"的校验(`a.b.c` 格式仍会校验)。 |
| `--tag <name>` | 否 | 推送时额外打的 tag 名,如 `release/v0.0.4`;不填则不打 tag。 |
| `--from <b>` / `--from-branch <b>` | 否 | 先从该远端分支 `fetch` 并 `checkout`,默认用当前工作区。 |
| `--to <b...>` / `--to-branch <b...>` | 否 | push 的目标分支,可空格分隔多个,默认 `main`。 |
| `--push` | 否 | 真正推送到 GitHub(默认关闭,相当于 `skip_push=true`)。 |
| `--no-branch` | 否 | 不切临时分支,原地在当前分支处理。 |
| `--token <pat>` | 否 | GitHub PAT;传了就走 HTTPS + token(等价于设置环境变量 `GITHUB_TOKEN`)。 |
| `--ssh` | 否 | 强制用 SSH 推送(默认即 SSH,此参数保留兼容)。 |
| `--force` | 否 | push 用 `--force-with-lease` 安全覆盖远端(解决 non-fast-forward)。 |
| `-h` / `--help` | 否 | 显示脚本头部的帮助信息。 |

### 推送认证(默认 SSH)

不传 token 时默认走 SSH,靠本机 SSH key 认证:

```bash
# 1) SSH(默认):免 token,靠本机 SSH key
bash .scripts/release_local.sh 0.0.4 --push

# 2) 环境变量:走 HTTPS
GITHUB_TOKEN=github_pat_xxx bash .scripts/release_local.sh 0.0.4 --push

# 3) 参数:走 HTTPS
bash .scripts/release_local.sh 0.0.4 --push --token github_pat_xxx
```

### 发布前剥离内部目录

`--push` 时,脚本会逐个(而非一次性)把 `.codebase/`、`.scripts/` 用
`git rm -r --cached --ignore-unmatch` 从索引剥离,然后**断言**它们确实已不在待推送内容中,
否则中止 push。这样能确保内部脚本不会被推到公开的 GitHub 仓库。

### 常见错误排查

#### `fatal: couldn't find remote ref release/v0.0.4`(无法找到远程引用)

**原因:** `--from`(或 `--to`)指定的分支在远端 `origin` 上**不存在**。最典型的场景是:
`release/vX.Y.Z` 分支已合入 `main`,而合并 MR 时勾选了"删除源分支",导致远端已无该分支;
其次是分支名拼写有误。

**脚本行为:** 脚本会在 `git fetch` **之前**先用 `git ls-remote` 探测该分支是否存在。
若不存在,直接打印可执行的修复建议并列出现有远端分支,而不是抛出难懂的
`couldn't find remote ref`。

**解决办法(任选其一):**

1. 用一个真实存在的分支作为 `--from`,例如 `--from main`;
2. 若确需该 release 分支,先基于主干重建再推送:
   ```bash
   git checkout -b release/v0.0.4 origin/main && git push origin release/v0.0.4
   ```
   (也可在 GitLab / 平台侧直接从 `main` 新建 `release/v0.0.4` 分支;)
3. 若本地已 checkout 到目标提交,去掉 `--from`,直接用当前工作区处理。

> 提示:执行发布前可先跑 `--doctor` 体检,它会提前报出 `--from` / `--to` 分支是否可达,
> 避免正式发布时才发现分支缺失。

#### `[Errno 2] No such file or directory`(找不到 prepare_release.py)

`--from release/vX.Y.Z` 等分支可能不含 `.codebase/` 目录,`checkout` 过去后
`prepare_release.py` 会随工作区消失。脚本已在切分支**之前**把它复制到仓库外的临时文件,
后续统一用该副本执行,不受切分支影响;若仍报错,请确认当前起始分支包含 `.codebase/`。
