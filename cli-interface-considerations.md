# Pacpar.CLI 设计：pacman 形状的界面与委派边界

> **状态：** CLI 动工前的设计调研，不是实现状态报告。文中的“应当”“建议”描述目标形态。
> **版本：** 2026-11-01 第四版。把事务评审的 TUI 推迟到 1.1，1.0 用文本摘要；
> `-R` 在 1.0 直通 pacman，1.1 接入删除评审。版本按 semver 标记。
> **范围：** `Pacpar.CLI` 应当提供什么，以及 `Pacpar.Alpm`、`Pacpar.Alpm.FSharp`、
> `RazorConsole.Elmish` 三个下层各自需要保证什么。

---

## 1. 结论摘要

CLI 语法应严格保持 pacman 的形状：`pacpar <operation> [options] [targets]`。操作沿用
`-S`、`-R`、`-Q`、`-U`、`-F`、`-D`、`-T`，选项字母与语义也一并沿用。让工具比
pacman 更友好的能力，应放进这套语法已经存在的流程内部，而不是在旁边另造一套界面。

委派边界按一个总原则确定： **机制归 libalpm，界面与编排归 Pacpar，构建归 makepkg，
keyring 归 pacman-key。** 安装、升级与 TUI 不经过 pacman 二进制：pacman 是前端，不是库；
事务一旦交出去，就无法在 prepare 与 commit 之间评审。纯查询、检查与安装原因修改是例外：
它们不需要 Pacpar 介入事务，直接以 argv 直通 pacman，换取精确的输出、locale、颜色与
检查语义。`-R` 在 1.0 也是例外：不做删除评审时直通 pacman，1.1 接入评审后再改由 Pacpar
发起。libalpm 提供的事务、依赖求解、文件安装、hook、scriptlet 与签名校验，仍是应当复用的
机制。

兼容性分成状态、配置、行为、输出四级。状态与配置必须一致；行为应当一致，例外必须可选
并有测试；输出分两种：直通命令由 pacman 原样产生，天然一致；Pacpar 自研的命令不承诺
人类可读逐字节一致，机器可读输出（`-F --machinereadable`、`--print`、`--print-format`）
承诺兼容。

有三个交互点值得 TUI 化：事务评审界面、选择器、AUR 评审队列。事务评审的 TUI 排到 1.1：
1.0 用文本摘要，AUR 评审用行模式，`-R` 直通 pacman。三者在无 TTY、被管道接管或
`--noconfirm` 时全部回退到行模式。RazorConsole.Core 能覆盖大部分组件；进度条与多选仍需
补组件或本地实现。`-Qi` 这类查询详情不做 TUI，直通 pacman 的文本输出。

原生多连接下载器通过 `FetchHandler` 接入，作为可选引擎，默认仍用 libalpm 的内部下载器。
接入前先对齐条件请求、断点续传、镜像回退、签名与沙箱语义。逐文件多连接会失去跨文件并行；
是否值得，按包体大小与链路实测决定。

---

## 2. 委派边界

很多 AUR helper 把写操作整个交给 pacman 二进制，自己只做查询与编排。这个做法在助手早期是
合理的：它把事务、锁、签名、hook 与 scriptlet 都留给 pacman，助手只需要拼参数。Pacpar 的
前提不同：它已经有一份覆盖全部 libalpm 入口的 C# 绑定，可以直接成为前端。于是问题从
“要不要调用 pacman”变成“哪些职责必须留在 libalpm，哪些必须由 Pacpar 自己承担”。

### 2.1 判断顺序

对每一项工作，按下面四个问题依次判断：

1. **它是否直接改变持久状态？** 包数据库、文件系统、`db.lck`、keyring——是，则机制必须
   由 libalpm、pacman-key 提供，不得自己重写。1.0 的 `-R` 是例外：不做删除评审时，允许
   把整个写事务交给 pacman；凡是要由 Pacpar 在 prepare 与 commit 之间插入评审的写事务，
   必须由 libalpm。自己重写状态机，等于重写依赖求解、冲突检测、文件事务与回滚。
2. **它是否有稳定的契约？** libalpm 的 C API、makepkg 的 `--printsrcinfo` /
   `--packagelist`、pacman-key 的退出码——有，则可以委派。查询命令的契约本来就是 CLI 输出，
   原样透传即可；不要把人类可读文本解析成数据，也不要把进度条文本当契约。
3. **它是否需要改变交互？** 如果价值来自“换一种问法”或“把提示组织成界面”，这层必须归
   Pacpar。libalpm 的 question 回调只给数据和默认答案，界面由前端决定。
4. **能否写等价性测试？** 能，就可以自研并在测试里对照；不能，就不要自研。

两个否决条件先于以上问题：

- 自研路径一旦失败就会损坏系统状态，或必须跳过签名校验才能工作——不得自研。
- 只能通过解析另一个程序的人类输出才能完成——不得委派并解析。解析人类输出是把对方的
  UI 当 API，版本一升就碎；原样透传 stdin/stdout 不在此列。

### 2.2 工作分类

| 工作                         | 归属                                                     | 理由                                                                                                       |
|------------------------------|----------------------------------------------------------|------------------------------------------------------------------------------------------------------------|
| 依赖求解、冲突检测、文件冲突 | libalpm                                                  | 求解器与文件数据库是 libalpm 的核心状态机；重写会引入与 pacman 不一致的解。                                |
| 事务与 `db.lck`              | libalpm                                                  | 锁的获取、事务阶段与失败清理属于 libalpm 的契约；前端只决定何时开始与提交。                                |
| 下载与签名校验               | libalpm（默认），可选原生引擎                            | 传输层可以替换；签名判定、必需/可选语义与失败处理必须与 libalpm 一致。                                     |
| 安装、删除、升级、降级、重装 | libalpm                                                  | 文件事务、备份文件、hook 与 scriptlet 的顺序不可由前端重排。                                               |
| hook 与 scriptlet            | libalpm                                                  | 运行时机与失败策略由 libalpm 决定；Pacpar 只负责展示输出与确认。                                           |
| 查询与本地数据库读取         | 普通形态直通 pacman；`-Qu`、`--aur` 等自研扩展走 libalpm | 普通 `-Q` / `-F` 的契约就是 pacman 的 CLI 输出；扩展形态要合并 AUR 数据或改呈现，必须自己组织。            |
| 安装原因修改                 | pacman（直通）                                           | `-D` 不进入 prepare/commit，也没有评审界面；直通沿用 pacman 的锁与 `-Dk` / `-Dkk` 检查。                   |
| `pacman.conf` 解析           | Pacpar                                                   | libalpm 不解析配置文件；仓库、SigLevel、缓存与 hook 目录必须由托管层装配。直通命令则交给 pacman 自己解析。 |
| 选项解析、退出码、提示渲染   | 直通命令归 pacman；自研命令归 Pacpar                     | 直通路径原样转发 argv 并继承退出码；自研路径才需要复刻提示与错误码。                                       |
| 事务评审、选择器、AUR 评审   | Pacpar                                                   | 交互层是产品差异所在；libalpm 不提供界面。                                                                 |
| PKGBUILD 解析、构建、打包    | makepkg                                                  | PKGBUILD 是 bash 程序；重新实现 source 与打包语义不可行。                                                  |
| keyring 初始化与更新         | pacman-key                                               | 这是 GnuPG 操作，不是 libalpm 事务。                                                                       |
| 提权                         | sudo 或 doas                                             | 环境策略，不是包管理逻辑；构建不提权，安装才提权。                                                         |

### 2.3 与“全委派给 pacman”的对比

| 维度       | 全委派给 pacman 二进制                | 直接用 libalpm                     |
|------------|---------------------------------------|------------------------------------|
| 事务原子性 | 一次 pacman 调用内原子；跨调用不原子  | 同一事务内原子                     |
| 锁         | pacman 自己取放                       | Pacpar 控制取放时机                |
| 提示       | 只能通过 stdin 喂答案或 `--noconfirm` | question 回调，可渲染任意界面      |
| 输出       | 解析人类可读文本                      | 结构化事件与返回值                 |
| 下载       | 只能 XferCommand 或 pacman 内部下载器 | 内部下载器或自定义 `FetchHandler`  |
| 兼容性     | 自动跟随 pacman                       | 需要自己维护                       |
| 运行时依赖 | 依赖 pacman 二进制                    | 只链接 libalpm，不启动 pacman 前端 |

全委派不是错误，而是不同的取舍。它在助手早期降低了实现成本，也让兼容性自动跟随 pacman。
代价是前端只能看到文本与退出码，无法在 prepare 与 commit 之间插入评审界面，也无法保持同一
个锁。Pacpar 的边界不必二选一：安装、升级与 TUI 走 libalpm（1.0 的 `-R` 例外，直通
pacman），纯查询、检查与安装原因修改直通 pacman，只有需要合并 AUR 数据的形态才自研。
判据见 2.4.1。运行时依赖也不是非此即彼；libalpm 由 pacman 包提供，直通查询不会再引入一个
新包。

### 2.4 安装/升级事务与 TUI 不调用 pacman 二进制

安装、升级、降级、重装事务与 TUI 不调用 pacman。1.0 的 `-R` 是例外：不做删除评审，
argv 直通 pacman；1.1 接入评审后改为自研。理由如下：

- **需要评审的事务路径上，pacman 是前端，不是库。** 它的选项、提示顺序与输出会随版本
  变化，不构成状态事务的 API。把前端当库调用，等于把事务路径的 UI 当 API。
- **`pacman(8)` 没有 EXIT STATUS 段。** 事务失败时脚本只能靠 0 与 1 推断结果；具体原因
  在人类可读输出里。Pacpar 需要结构化错误，不能依赖这种接口。
- **无法保持锁与事务边界。** 把事务交给 pacman，Pacpar 无法在 prepare 与 commit 之间插入
  评审界面，也无法保证两次调用之间系统状态没有变化。
- **`--print` 只打印计划。** 它不能执行事务，也不能替代 API；`pacman-conf` 是诊断工具，
  不是配置读取接口。
- **兼容性对照测试仍然需要 pacman 二进制。** 同一组操作分别跑 pacman 与 Pacpar，比较包
  数据库、文件清单与退出码。这是测试用途；直通查询与 1.0 的 `-R` 把这种对照关系带进了
  运行路径，但那是查询/删除路径的取舍，不是需要评审的事务路径。

### 2.4.1 查询与 1.0 删除的直通

判据只有两条：这条命令是否需要在 prepare 与 commit 之间插入 Pacpar 的逻辑；结果是否
需要合并 AUR 数据。两者都否，就直通 pacman。`-V` 与 `-h` 除外，它们打印 Pacpar 自己的
版本与帮助。

| 命令                                   | 处理        | 理由                                                                                                                                  |
|----------------------------------------|-------------|---------------------------------------------------------------------------------------------------------------------------------------|
| `-D`、`-F`、普通 `-Q`、`-T`            | argv 直通   | 不需要事务评审，也不需要合并 AUR；输出、locale、颜色、`-Qk` / `-Qkk`、`-Qo` 的符号链接语义、`-F --machinereadable` 都由 pacman 保证。 |
| `-Qu`、`-Qm --aur`、`--aur`、`--devel` | 自研        | 要合并仓库与 AUR 的升级列表，或过滤出 AUR / 开发版包。                                                                                |
| `-S`、`-U`（含 `-Ss`、`-Si`）          | 自研        | 安装、升级与构建要合并 AUR、使用原生下载器与文本摘要；`-Ss` / `-Si` 要合并 AUR 搜索与信息。                                           |
| `-R`                                   | 直通（1.0） | 不涉及 AUR 与下载；1.0 不做删除评审，直通省掉锁、反向依赖、hook 与 question 复刻。1.1 接入删除评审后再改自研。                        |
| `-V`、`-h`                             | 自研        | 打印 Pacpar 自己的版本与帮助，不是 pacman 的身份。                                                                                    |

直通不是把 pacman 当库，也不是解析它的输出，而是把 argv 原样交给它。Unix 上用 `exec`
替换进程，保留 stdin/stdout/stderr、TTY、颜色、locale 与退出码；用 `Process.Start` 时
至少不要重定向输出。`-Qi` 的详细字段不做 TUI；直通反而保留 pacman 的终端宽度换行与颜色。

`-R` 是最容易误判的一项：它不涉及 AUR，也不下载，所以 1.0 直接交给 pacman 是合理的。
删除仍是一次写事务，但只有接入评审时才需要 Pacpar 介入 prepare 与 commit；1.0 不做删除
评审，代价是绕开 Pacpar 的提权与错误呈现，直接继承 pacman 的 y/N 与退出码。1.1 再把
`-R`、`-Rns` 与清理陈旧依赖的场景接入同一个评审模型。

直通写命令（`-D`、`-R`）前，Pacpar 按 2.5 的提权规则用 sudo/doas 启动 pacman；pacman
不会自己提权。

直通范围里有两个容易踩的点。`-D` 是写操作，但没有 prepare/commit 评审；直通省掉锁与
`-Dk` / `-Dkk` 的复刻。如果坚持“状态修改一律由 Pacpar 发起”，就保留 `-D` 原生，只直通
`-F` 与 `-Q`。`-Fy` 会走 pacman 的下载路径，不经过 2.8 的原生下载器；这可以接受，因为
它只更新 `.files` 数据库。

实现上还要处理递归：如果 `pacman` 的 realpath 指向 `pacpar` 自己，报错而不是递归执行。
Pacpar 的 `--aur`、`--devel`、`--skipreview` 一旦出现，就转到自研路径，不能混进直通
argv。

### 2.5 例外：必须调用的外部程序

- **pacman（查询兼容直通，以及 1.0 的 `-R`）。** `-D`、`-F`、普通 `-Q` 与 `-T` 原样
  转交，`-R` 在 1.0 也直通，见 2.4.1。`-R` 是一条受控的临时例外：它不经过 Pacpar 的
  评审，但仍是写事务，锁与依赖检查由 pacman 负责，提权由 Pacpar 通过 sudo/doas 请求；
  1.1 接入删除评审后改为自研。
  查询直通不包含 prepare/commit 评审，也不合并 AUR 数据。
- **makepkg。** PKGBUILD 是 bash 程序。重新实现 source、变量展开、`prepare()`、
  `build()`、`package()` 与拆分包的语义，等于实现一个 bash 子集；既不可行也不安全。
  Pacpar 只调用 makepkg，并解析它有文档的机器输出：`--printsrcinfo`、`--packagelist`、
  `--verifysource` 的退出码。注意 `--printsrcinfo` 会 source PKGBUILD，只能在评审之后
  调用。
- **pacman-key。** keyring 的初始化、populate、refresh 与信任模型是 GnuPG 操作，不是
  libalpm 操作。libalpm 通过 gpgme 验签，但不管理 keyring。Pacpar 调用 pacman-key，保持
  与 Arch 安装介质一致。
- **sudo 或 doas。** 提权是环境策略。Pacpar 只负责在需要时请求，并保持“构建不提权、安装
  才提权”的边界。
- **GPG。** 不直接调用。验签由 libalpm 完成；只有 keyring 管理走 pacman-key 或 gpg。

### 2.6 兼容性四级

| 级别 | 范围                                                                                                  | 承诺                                                     |
|------|-------------------------------------------------------------------------------------------------------|----------------------------------------------------------|
| 状态 | 包数据库、文件系统、安装原因、hook 结果                                                               | 必须一致                                                 |
| 配置 | `pacman.conf` 的仓库、SigLevel、CacheDir、HookDir、IgnorePkg、Architecture                            | 必须一致                                                 |
| 行为 | 提示顺序、默认答案、`--noconfirm`、`--print`、部分升级拒绝                                            | 应当一致；例外必须可选并有测试                           |
| 输出 | 直通命令的 stdout/stderr；自研命令的人类可读文本；`-F --machinereadable`、`--print`、`--print-format` | 直通命令原样；自研人类可读不承诺逐字节；机器可读承诺兼容 |

输出一级最容易被误解。直通命令的输出直接由 pacman 产生，不需要另立契约；自研命令的
人类可读文本可以改进，但机器可读输出是脚本接口，不能改。新增 token 只能追加，不能改变
既有字段的含义。状态与配置是硬约束；行为是软约束，但偏离要在 `--help` 或发布说明里写明。

### 2.7 选项分类

| 类别         | 例子                                                                                                             | 处理                                                                                    |
|--------------|------------------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------|
| libalpm 选项 | `--dbpath`、`--root`、`--arch`、`--cachedir`、`--logfile`、`--gpgdir`、`--hookdir`、`--disable-download-timeout` | 直接映射 `alpm_option_set_*`                                                            |
| 事务标志     | `-d`、`-dd`、`--nodeps`、`--needed`、`--asdeps`、`--asexplicit`、`--dbonly`、`-w`                                | 映射 `TransactionFlags`；两个易错标志的语义见下表后的说明                               |
| 前端策略     | `--noconfirm`、`--confirm`、`--print`、`--print-format`、`--noprogressbar`、`-q`、`--ask`                        | 自研命令由 Pacpar 实现，直通命令交给 pacman；`--print` 必须在 prepare 后、commit 前返回 |
| 查询呈现     | `-i`、`-l`、`-s`、`-o`、`-c`、`-p`、`-k`                                                                         | 普通 `-Q` / `-F` 直通 pacman；`-Qu`、`--aur`、`--devel` 自研                            |
| 助手扩展     | `--aur`、`--devel`、`--skipreview`、`-P` 形状                                                                    | Pacpar 定义；不改变 pacman 选项的语义                                                   |
| 危险选项     | `--overwrite`、`--assume-installed`、`-dd`、`--ignore`、`--nodeps`                                               | 逐项决定；必须显式，不能默认开启                                                        |

事务标志里有两个容易写错的语义。`-d` 与 `--nodeps` 共用 pacman 的同一个分支：第一次出现只设
`NoDepVersion`，第二次出现才追加 `NoDeps`。`--dbonly` 不是只跳过文件写入，它同时设置
`DBONLY`、`NoScriptlet` 与 `NoHooks`。

`--print` 有一个容易漏掉的细节：prepare 仍可能触发 question。pacman 在 `--print` 下对
`ALPM_QUESTION_INSTALL_IGNOREPKG` 与 `ALPM_QUESTION_REPLACE_PKG` 回答 1，其余回答 0。
Pacpar 要复刻这套默认值，否则脚本拿到的计划会变。

### 2.8 原生下载器

#### 2.8.1 现状

libalpm 在 `handle->fetchcb == NULL` 时使用内部 libcurl。`download_files` 为每个候选包
构造 payload，`_alpm_download` 用 `curl_multi` 并行传输，并行度来自
`ParallelDownloads`；每个文件一个传输句柄。已有缓存文件时，curl 路径用文件 mtime 发
`If-Modified-Since`；`force` 为真时跳过条件请求；`.part` 文件存在且 `allow_resume`
时用 Range 续传；镜像按 cache server、再按 server 顺序回退；包成功后再取同镜像的 `.sig`。
启用 `DownloadUser` 时，内部下载在降权子进程中运行，并施加文件系统与系统调用限制。

XferCommand 是 pacman 前端的键。设置后 pacman 注册 `fetchcb`，libalpm 改为逐个 payload
调用外部命令。它拿不到 mtime，无法发条件请求；只做降权与 `no_new_privs`，没有文件系统与
系统调用限制；进度只能靠外部工具自己的输出；下载逐个进行。因此 XferCommand 不是实现原生
下载器的路径。

Pacpar 的绑定已经暴露 `FetchHandler`、`FetchResult`、`PackageBase.Filename`、
`PackageBase.DownloadSize`、`Database.Servers` / `CacheServers`。缺的是包到所属同步
数据库的公开关联；构建下载计划时要么补 API，要么按文件名遍历同步库。

#### 2.8.2 回调契约

`FetchHandler` 的类型是 `Func<string, string, bool, FetchResult>?`：参数依次是 URL、
下载目录、是否强制。libalpm 对每个包文件至少调用一次：包本身有单一 URL 时调用一次；否则按
cache server、再按 server 的顺序逐次调用，直到某个镜像返回非失败。包取到后，按同一镜像
再取 `.sig`（如果 SigLevel 要求）。回调收到的是下载目录：未启用沙箱时它就是缓存目录；
启用沙箱时它是缓存目录下的临时子目录，下载结束后由 libalpm 搬回缓存目录。

返回 `Success` 表示已下载，`UpToDate` 表示本地文件已存在且相同，`Error` 表示失败。
必需签名抓取失败会让事务失败；可选签名失败由 libalpm 忽略。`UpToDate` 不会触发重新下载
或内容比对；后续 `check_validity` 仍会做签名校验。

libalpm 逐个包文件调用回调，不会并行调用。因此：单文件多连接可以直接做；跨文件并发只有
在第一次回调前就知道完整计划时才可能。

`FetchHandler` 是会话级设置。设置后，数据库更新与 `AlpmSession.FetchPackageUrls` 也会走
这个回调；不要只在事务期间假设它只影响包下载。数据库更新因此失去内部 curl 的条件请求，
要么让引擎实现同样的语义，要么只在包下载阶段临时设置回调。绑定里的 `DownloadHandler`
明确写着：在 `FetchHandler` 配置期间不触发。原生引擎必须自己发进度事件。

#### 2.8.3 接入方式比较

| 方式                                    | 跨文件并行           | 需要下载计划             | 对 libalpm 的侵入 | 适用                   |
|-----------------------------------------|----------------------|--------------------------|-------------------|------------------------|
| XferCommand                             | 否                   | 否                       | 无（前端键）      | 拒绝                   |
| `FetchHandler` 逐文件                   | 否；只能单文件多连接 | 否                       | 小                | 阶段一，验证引擎正确性 |
| `FetchHandler` 首次回调批量             | 是                   | 是（读 `AddedPackages`） | 中                | 阶段二，推荐方向       |
| prepare 后预下载，提交时返回 `UpToDate` | 是                   | 是                       | 中                | 阶段二备选             |
| 独立 `-w` 事务                          | 是                   | 是                       | 大（双事务）      | 测试或显式 `-Sw`       |

表里的“跨文件并行”只指包与包之间。单文件多连接在任何方式里都能做，但它对大包有意义，
对大量小包反而更慢。`-Syu` 的典型负载是很多小包，跨文件并行才是主要收益。

“首次回调批量”的做法是：第一次回调时读取 `Transaction.AddedPackages`，按
`Filename` 与同步库构造完整计划，启动所有下载，等本批完成后再返回；后续回调发现文件
已就绪，返回 1。它不需要新的公开 API 也能做（按名字扫描同步库），但包到库的关联会让
计划更可靠。回调运行在事务线程上，读取 `AddedPackages` 不能跨线程调用 libalpm。

“prepare 后预下载”的做法是：在 prepare 之后、commit 之前用 `AddedPackages` 与
`Database.Servers` 下载全部文件（含 `.sig`）；提交时临时设置 `FetchHandler`，对已存在
的文件返回 `UpToDate`，让 libalpm 跳过实际传输。副作用是 `download_size` 在 prepare
时已经算好，commit 仍会把包列入候选并继续校验；`UpToDate` 只是让回调不发起请求。锁的
持有时间与普通 commit 相同，因为下载本来就在 commit 内。

独立 `-w` 事务利用 `ALPM_TRANS_FLAG_DOWNLOADONLY`：`_alpm_sync_load` 在下载与校验后
直接返回，不安装。于是可以先跑一个下载事务，再跑安装事务。但两次 prepare 会重复求解，
两个事务之间有窗口，另一个 pacman 可能改变状态。只适合测试与显式 `-Sw` 兼容，不作为
默认流水线。

#### 2.8.4 推荐阶段

| 阶段 | 内容                                  | 验收                                     |
|------|---------------------------------------|------------------------------------------|
| 0    | 默认 libalpm；只加基准与开关          | 记录大包、小包、差链路三组基线           |
| 1    | `FetchHandler` 单文件多连接，默认关闭 | 字节一致、断点续传、签名、失败回退       |
| 2    | 批量计划，默认关闭                    | 跨文件并行、镜像回退、状态与默认下载一致 |
| 3    | 评估是否默认                          | 两组基准无回归才切换默认                 |

配置键建议使用 `[options] DownloadEngine = libalpm|native|auto` 与
`ConnectionsPerFile = N`。命令行对应 `--download-engine`。`auto` 只在实测有收益时选择
原生引擎，否则走 libalpm。保留 `--download-engine=libalpm` 作为无条件回退。

#### 2.8.5 等价性清单

原生引擎必须逐项对齐下面的行为，缺一项就可能在特定镜像上失败：

- **条件请求。** 对已存在文件发 `If-Modified-Since`，304 视为成功；不要无条件重下。
- **断点续传。** `.part` 存在且服务器支持时用 Range；服务器不支持时从头开始。
- **镜像回退。** cache server 优先，然后 server；单个镜像失败继续下一个；记录最终成功的
  镜像，签名从同一镜像取。
- **签名。** 包成功后抓同镜像的 `.sig`；SigLevel 要求时缺失即失败；可选时忽略。
- **强制。** 回调参数为真时忽略本地文件。
- **代理与 TLS。** 读取 `http_proxy`、`https_proxy`、`no_proxy`、CA 路径与
  `--disable-download-timeout`；行为与 libcurl 一致。
- **沙箱。** 启用 `DownloadUser` 时降权到该用户，并施加与 libalpm 相同的文件系统与系统
  调用限制；做不到就不要在启用沙箱时使用原生引擎。
- **缓存路径。** 写入 libalpm 选出的缓存目录；回调给的暂存目录可能是临时目录，不要假设
  最终路径。
- **进度。** 自己发事件；不要依赖 `DownloadHandler`。
- **原子性。** 先写 `.part`，成功后 rename；失败保留 `.part` 供续传。
- **校验。** 让 libalpm 的 `check_validity` 做最终签名校验；引擎可以在下载后做一次早期
  校验，但不能替代。
- **线程。** 下载线程不调用 libalpm；libalpm 只在持有事务的线程上调用（ADR 0011）。

#### 2.8.6 收益与代价

原生引擎的收益必须实测，不能从“多线程”推断。按包体大小分层：小于 1 MiB、1 到 50 MiB、
大于 50 MiB；按镜像 RTT 分层；比较 wall time、连接数、失败率与重试次数。只有典型
`-Syu` 包含大包时，单文件多连接才有明显收益；大量小包时，跨文件并行比单文件多连接重要。

代价是维护条件请求、续传、镜像、签名、代理、TLS 与沙箱的等价性。任何一项偏差都会表现为
难查的下载失败或签名失败。默认值必须是 libalpm；原生引擎用显式开关启用，并保留回退。

#### 2.8.7 与 F# 设计文档的偏差

`src/Pacpar.Alpm.FSharp/DESIGN.md` 第 7.3 节把 Model B 描述为“在 `trans.Commit()` 前
预下载，libalpm 会跳过已存在的包与签名”。这个说法只在文件于 prepare 之前已在缓存时成立：
`compute_download_size` 在 prepare 末尾执行，之后 `download_size` 不再重算。prepare 之后
预下载，commit 仍会把包列入下载候选。

修正方式有两种：提交时临时设置 `FetchHandler` 对已存在文件返回 `UpToDate`，或接受每个
文件一次条件请求。本文建议前者，并把 `pendingDownloads` 的语义改成“prepare 后仍需要下载
的目标”，而不是“commit 会跳过的目标”。另外，`DownloadTarget.Repository` 需要包到同步库
的公开关联；没有它，下载计划无法可靠地选择 SigLevel 与镜像。

### 2.9 一句话规则

凡是要由 Pacpar 插入评审或编排的写事务，走 libalpm；1.0 的 `-R` 不做删除评审，直通
pacman；keyring 走 pacman-key；凡是要决定自研命令“怎么问、怎么显示、怎么组织”的，走
Pacpar；凡是要执行 PKGBUILD 的，走 makepkg。安装、升级与 TUI 不经过 pacman；纯查询、
检查、安装原因修改与 1.0 的 `-R` 直通 pacman。原生下载器是唯一允许绕过 libalpm 默认实现
的部件，因为它只替换传输层，不改变事务语义。

---

## 3. 要保留的 CLI 形状

### 3.1 语法

```bash
pacpar <operation> [options] [targets]
```

操作沿用 pacman：`-S`（同步）、`-R`（删除）、`-Q`（查询）、`-U`（安装文件或 URL，
构建好的 AUR 产物也从这里进入）、`-F`（文件）、`-D`（数据库）、`-T`（依赖测试）、
`-V`、`-h`。不为友好化功能新增动词。

助手特有的附加项沿用 paru 的 `-P` 先例：用一个辅助操作承载不触碰数据库的功能
（`--news`、`--stats`、`--complete`、`--gendb`），其余放在长选项里。语法无法表达的
东西——包括以后的历史与撤销——可以先考虑放进 `-P` 形状的位置（见第 7 节）。

### 3.2 选项兼容性

匹配 pacman 的字母与语义。评审（1.0 文本摘要、1.1 TUI）改变的是确认 **长什么样**，而
不是某个标志 **表示什么**。1.0 就值得承诺的候选项如下：

| 选项                                                         | 处理方式                 | 说明                                     |
|--------------------------------------------------------------|--------------------------|------------------------------------------|
| `--noconfirm`                                                | 完全一致                 | 跳过确认，直接自动同意                   |
| `-y` / `-u` / `-s` / `-i` / `-l` / `-o` / `-c` / `-d` / `-w` | 完全一致                 | 核心同步与查询标志                       |
| `-q`、`--noprogressbar`                                      | 完全一致                 | 脚本用的静默输出控制                     |
| `--print`、`--print-format`                                  | 完全一致，可追加新 token | 机器可读预览，也是确认路径的行模式对应物 |
| `--asdeps`、`--asexplicit`、`--needed`                       | 完全一致                 | 安装原因与已安装跳过                     |
| `--dbpath`、`--root`、`--config`、`--logfile`、`--cachedir`  | 完全一致                 | 测试与容器场景不可让步                   |
| `--overwrite`、`--assume-installed`、`-dd`、`--ignore`       | 逐项决定子集             | 危险选项是否提供，要有明确决定           |

`-D`、`-F`、普通 `-Q`、`-T` 与 1.0 的 `-R` 的选项由 pacman 自己解析；直通时 argv
原样转发。Pacpar 的 `--aur`、`--devel`、`--skipreview` 一旦出现，就离开直通路径，不能
混进 pacman 的 argv。

自研事务在 1.1 改用 TUI 时最容易丢掉的两个兼容点：

- **提示可应答。** 脚本靠管道输入回答 pacman 的编号提示（`"1\n"`）。选择器的外观可以
  不同，但 stdin 上必须继续接受同样的数字答案；`--noconfirm` 必须让结果确定（pacman 选
  默认项，新工具照做）。
- **退出码。** 直通命令直接继承 pacman 的语义（0 成功、1 失败、130 收到 SIGINT）；
  自研命令必须复刻同一套。`docs/adr/0003-stable-failure-codes-for-fsharp.md` 为 F# 层定义了
  稳定的语义失败码，可作为映射来源；该 ADR 目前只是设计决定，F# 层尚未实现（见 5.2 节）。

另有一个容易忽略的点：`--print` 下 prepare 仍可能提问。pacman 对忽略包与替换包回答 1，
其余回答 0。复刻这套默认值，见 2.7 节。

### 3.3 一个待决问题：裸调用

pacman 在裸调用时打印用法；常见助手在裸调用时执行完整升级。这是定位选择：如果 Pacpar.CLI
定位为 pacman 的替代品，就打印用法，保持形状纯净；如果定位为助手，裸调用等于完整升级，
更符合用户预期。两种做法都不应该进入全屏界面。

---

## 4. TUI 化价值落在哪里

下面三个界面都是 pacman **已经用更差的形式做过**的事（一个 y/N 提示、一个编号列表、
什么都没有），所以语法始终不变，改变的只是一个既有交互点的渲染方式。

### 4.1 事务评审：1.0 文本摘要，1.1 TUI

1.0 不做 TUI 评审，但事务摘要仍是必需：打印安装、升级、降级、重装与删除分组，附下载
体积与磁盘净变化，然后使用 pacman 风格的 y/N。它是 1.1 TUI 的行模式对应物，也是
`--print` 之外的人类可读确认。

1.1 用评审界面替换 `:: Proceed with installation? [Y/n]`：

- 分区表格：删除、安装、升级、降级、重装分组显示；旧版本到新版本的差异按段着色；每包给出
  体积变化；底部一行汇总（总下载体积、磁盘净变化）。
- 数据来源：`Transaction.AddedPackages` 与 `Transaction.RemovedPackages` 已提供事务成员
  的借用视图；旧版本与体积需要再关联本地数据库补齐；来源库名需要包到同步库的关联，目前
  还没有公开 API（见 5.1 节）。
- RazorConsole 组合：`Panel`（标题 “Pending changes”）承载 `SpectreTable` 分区，
  `Markup` 渲染带样式的差异片段，`ViewHeightScrollable` 处理长事务，底部放确认栏。
  `ModalWindow` 留给危险确认（见 4.4 节）。
- 优先用内联视口，而不是备用屏幕，让 shell 的滚动历史保持可见。RazorConsole.Core 把
  `ConsoleLiveDisplayOptions.UseAlternateScreenBuffer` 设为 `false` 时不会进入备用屏幕，
  但渲染仍是视口内绝对定位的增量更新，不是把界面插入滚动区。这个模式与 shell 历史的实际
  交互要在 CLI 阶段做最小原型确认，不要只依赖文档描述。
- 降级规则：输出不是 TTY，或传入了 `--noconfirm` → 退回 pacman 风格的文本报告加普通 y/N
  提示（或自动同意）。被管道接管但没传 `--noconfirm` → 照旧显示文本报告，从 stdin 读答案，
  行为与 pacman 一致。任何情况下都不能要求 TUI 才能完成事务。

1.0 的 `-R` 不进入这个模型，直接直通 pacman；1.1 再把删除组接入评审，优先覆盖 `-Rns`
与清理陈旧依赖的场景。

F# 设计里已经有这个界面的模型：`PreparedTransaction` 在持有 `db.lck` 时暴露只读的
`TransactionSummary`，用户确认之后才调用 `Commit`（`src/Pacpar.Alpm.FSharp/DESIGN.md`
第 2 节）。1.0 的文本摘要与 1.1 的 TUI 共用这个模型，因此 `TransactionSummary` 要宽到
足以直接渲染（见 5.2 节）。

### 4.2 选择器

提供者选择（`AlpmQuestion.SelectProvider` 回调）、包组选择、多版本选择，目前在 pacman 里
都是裸编号提示。把它们渲染成 `Select<T>` 菜单（可聚焦列表、键盘导航、格式化器），并在
仓库版本与归档版本同时存在时，扩展成版本浏览。

- `Select<T>` 是单选，pacman 的包组提示是多选。要么在 Elmish 模型里用 `Rows` 加选中
  状态拼出多选，要么把 `MultiSelect<T>` 记进组件缺口清单（见 5.3 节）。
- 数字答案必须继续可用（见 3.2 节）。选择器只是渲染升级，不是新协议。

### 4.3 AUR 评审队列（差异点）

1.0 先用行模式：把 PKGBUILD、`.install` 与 `.patch` 打到 stdout，逐包等待 y/N。TUI
队列随后再做。事务里包含 AUR 包时，提交前的阶段应按包走完评审义务，再允许 source 或构建：

- 每包一个面板：PKGBUILD、`.install`、`.patch` 文件；对照上次安装的构建给出
  `git show` 风格的 diff。现有助手已经提供 diff；这里的优势是把它们组织成一条连续评审
  队列，而不是散落的提示。
- RazorConsole.Core 的 `SyntaxHighlighter`（ColorCode、行号、主题）直接适配 PKGBUILD 与
  diff 渲染；`Markdown` 用来显示 AUR 元数据与评论。
- 来自 Arch 政策的硬规则：评审之前不得 `source` 或执行任何内容；`.SRCINFO` 与 AUR RPC
  是元数据来源；对 AUR 包使用 `--noconfirm` 时，仍应要求显式选择跳过评审
  （`--skipreview` 先例），不能静默构建。
- 降级：没有 TTY 时，行模式把文件与 diff 打到 stdout，逐包等待 y/N，也就是常见助手的
  行为。

### 4.4 危险确认

删除基础包或系统包、以及 `--overwrite` 一类操作时，要求用户输入指定短语。`ModalWindow`
加 `TextInput` 可以覆盖；行模式回退就是通过 stdin 输入同一短语。这个模式的价值在于让
不可逆操作与普通确认区分开，而不是增加一次无意义的回车。

### 4.5 进度与日志流

- F# 事件架构已经朝这个方向设计（`src/Pacpar.Alpm.FSharp/DESIGN.md` 第 6 节）：
  `EventSink.toChannel` 把关键生命周期事件（无界）与进度（有界、合并、容量 1）分开。
  这个合并通道正好提供终端渲染器需要的背压。
- RazorConsole.Core **没有进度条或多进度组件**（只有 `BarChart` 与 `Spinner`）。可选方案：
  用带样式的 `Markup` 进度条放进 `ViewHeightScrollable` 自行拼装，或者在计划里加入
  `ProgressBar` / `MultiProgress` 组件。这件事要在 CLI 阶段之前定下来，否则下载界面
  只能手搓。
- 渲染单次快照有可观测的开销，按 16 到 33 ms 的节拍合并更新即可摊薄。合并通道已经免费
  提供这层节流，不要绕过它改成逐回调调度。
- 安装阶段的日志输出（libalpm 的 hook 与 scriptlet 输出）以追加行的形式出现在固定的进度条
  上方。用 RazorConsole 实现就是一组日志行 `Rows` 加一行状态，按合并后的节拍更新。

### 4.6 查询与直通输出

`-D`、`-F`（含 `-Fl`）、普通 `-Q` 与 `-T` 直通 pacman，stdout/stderr 原样。`-Qi` 的详细
字段不做 TUI；直通保留 pacman 的终端宽度换行与颜色。`-Qu`、`-Ss`、`-Si` 仍由 Pacpar
组织，因为要合并 AUR；`-Qu` 的输出要对齐 pacman 的 ` -> newver` 与 `[ignored]`，`-Ss`、
`-Si` 的格式也要与 pacman 一致。

直通路径不做分页器；需要分页时由 shell 管道或 `less` 负责。`--print-format` 仍是事务的
机器接口，不是查询接口。

---

## 5. 下层需要保证什么

以下契约决定第 4 节在 CLI 动工时是便宜还是昂贵。

### 5.1 `Pacpar.Alpm`（C# 绑定）

截至 2026-10-31，P/Invoke 层覆盖全部 236 个 libalpm 入口；高层 API 在排除刻意省略后覆盖
84.1%（`src/Pacpar.Alpm/COMPLETENESS.md`）。与 CLI 流程直接相关的能力状态如下：

| 能力                                 | 当前状态（2026-10-31）                                                                                                                                                              |
|--------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 事务与包列表                         | `Transaction.Prepare`、`Commit`、`AddedPackages`、`RemovedPackages` 已提供。后两者是借用视图，事务释放后失效。                                                                      |
| 数据库更新与镜像                     | `Database.Update`、`AlpmSession.UpdateDatabases`、`Database.AddServer` / `RemoveServer` / `SetServers` 已封装。                                                                     |
| 依赖与冲突检查                       | `AlpmSession.CheckDependencies`、`CheckConflicts` 已封装。                                                                                                                          |
| 目标解析与搜索                       | `Database.Search`、`PackageView.FindSatisfier`、`AlpmSession.ResolveDependency` 已封装。                                                                                            |
| 版本比较                             | `PackageVersion` 基于 `alpm_pkg_vercmp`。                                                                                                                                           |
| 下载接入                             | `FetchHandler`、`FetchResult`、`PackageBase.Filename`、`PackageBase.DownloadSize`、`Database.Servers` / `CacheServers` 已提供。`DownloadHandler` 在 `FetchHandler` 配置期间不触发。 |
| 安装原因                             | `PackageView.SetReason` / `InstallReason` 已提供。`-D` 直通 pacman 时由 pacman 取锁；若保留原生 `-D`，该调用本身不取 `db.lck`，Pacpar 必须补同样的锁边界。                          |
| `pacman.conf` 解析                   | **仍缺失**。libalpm 不解析它，托管层必须自行实现 sections、servers、SigLevel、cache 与 hook 目录，否则 `--config` 无法按 pacman 的行为工作。                                        |
| 包到来源数据库的公开关联             | **仍缺失**。`alpm_pkg_get_db` 只在内部使用；公开 API 只有 `PackageOrigin`（File / LocalDatabase / SyncDatabase），拿不到 `extra/ripgrep` 这样的库名。评审界面与下载计划都需要它。   |
| `PackageOperation*` 事件中的空包指针 | 已修复。`NewPackage` 与 `OldPackage` 是可空属性，安装与删除事件不再解引用空指针。                                                                                                   |

`FetchHandler` 是会话级设置。设置后，数据库更新与 `AlpmSession.FetchPackageUrls` 也会走
这个回调；不要只在事务期间假设它只影响包下载。已知但尚未修复的缺陷见
`reports/open-defects.md`，这里不重复。

### 5.2 `Pacpar.Alpm.FSharp`（F# 表面）

F# 层目前仍是模板桩：`src/Pacpar.Alpm.FSharp/Library.fs` 只有 `Say.hello`，
friend-assembly 桥接、稳定失败码与 `Result` 映射都未实现，也没有 F# 测试项目
（`reports/open-defects.md` 第 11 条）。以下是设计文档给出的、CLI 所依赖的契约：

- `TransactionSummary` 要宽到能直接渲染 4.1 节，不必回头再查 libalpm。当前设计只有
  `PackagesToInstall`、`PackagesToRemove`、`TotalDownloadSize`、
  `TotalInstallSizeChange` 四个字段；评审界面还需要每包的操作类型、旧版本字符串、下载
  体积、安装体积变化与来源数据库名。它就是评审界面的视图模型。
- 事件结构（`[<Struct>] ProgressInfo` 等）与拆分通道的设计是 UI 输入契约。CLI 消费
  `EventSink.toChannel`，不直接接原始回调。原生下载器的进度也应进入同一通道，而不是另开
  一套事件。
- 稳定失败码（`docs/adr/0003-stable-failure-codes-for-fsharp.md`）负责映射退出码与错误
  渲染。按 `GLOSSARY.md` 的词义，对外呈现“ALPM operation failure”及其“Failure detail”
  （缺失依赖、文件冲突已由 `AlpmTransactionException` 快照保存），“Query miss”保持
  非失败语义。
- `DESIGN.md` 第 7.3 节的 Model B 预下载假设需要按 2.8.7 节修正；否则实现会以为 commit
  会自动跳过 prepare 之后下载的文件。

### 5.3 `RazorConsole.Elmish`

“完整映射 RazorConsole.Core”是正确的目标线。对 CLI 而言，映射应优先落地 4.1 至 4.5 节
用到的组件：`ViewHeightScrollable`、`Scrollable`、`Select<T>`、`TextInput`、
`SpectreTable` 一族、`ModalWindow`、`Markup`、`Panel` / `Rows` / `Columns` /
`FlexBox`。

有三样东西映射不会提供，必须有人负责（放在 Elmish 层还是 CLI 层，需要决定）：

1. **按键与模式层。** 引擎没有快捷键路由，也没有模式机（vim 风格模式只能放进 Elmish 的
   `Model` / `Update`）。评审界面需要 y/n/q/`Ctrl-C` 以及 `/` 搜索语义。
2. **多选**（4.2 节）。`Select<T>` 只支持单选。
3. **进度渲染**（4.5 节）。引擎没有进度条组件。

另外，把渲染成本契约（合并与节流）写成文档指引或辅助函数。第 4 节里每个流式界面都依赖它。

---

## 6. AUR 助手功能

这部分不在 libalpm 内，而是它旁边的一层。组成如下：

1. **AUR RPC v5 客户端**：`info` / `search` 元数据，包括版本、依赖、投票、过期标志。
2. **`.SRCINFO` 解析器**：规划阶段读 AUR 仓库自带的 `.SRCINFO`，它不需要执行
   PKGBUILD。`makepkg --printsrcinfo` 会 source PKGBUILD，只能在评审通过后调用。解析器
   要处理拆分包、架构约束、`depends` / `makedepends` / `checkdepends`。
3. **混合依赖求解器**：在 sync 数据库缓存与 AUR 结果之上建一张图，给出拓扑构建顺序。
4. **Git 缓存**：克隆与拉取 `https://aur.archlinux.org/<pkg>.git`，保留上一次构建的树供
   4.3 节的 diff 评审。
5. **权限分离的 `makepkg` 运行器**：无特权构建（ArchWiki 要求），只在最后的
   `Transaction.AddPackage(loaded)` 加 `Commit` 提权；长时间构建采用 `--sudoloop`
   风格的行为。构建前用 `--verifysource` 确认源码与校验和，构建后用
   `--packagelist` 取产物路径。
6. **产物导入**：`AlpmSession.LoadPackage` 与 `Transaction.AddPackage` 已经绑定，还差一个
   面向拆分包的批量导入辅助。不调用 `--packagelist` 时，才需要从
   `pkgname-pkgver-pkgrel-arch.pkg.tar.zst` 从右切段解析；拆分包的 `pkgname` 可能含
   连字符，容易切错，优先用 `--packagelist`。
7. **标志策略**：不做 `-Sy` 部分升级（只允许 `-Syu` 组合），不做 `--ask` /
   `--nodeps` 这类欺骗 pacman 的动作。如果 AUR 层在组合升级菜单里允许跳过仓库更新，必须
   大声警告——部分升级是“系统损坏”的典型故障模式（见
   [System maintenance](https://wiki.archlinux.org/title/System_maintenance#Partial_upgrades_are_unsupported)）。
   `--noconfirm` 不应静默跳过 AUR 评审；必须显式传 `--skipreview`。
8. **devel 与 `-git` 包跟踪**：值得在早期留一个设计位置，哪怕很晚才实现。它需要状态
   文件，并且会碰到 `-Qu` 的代码路径。

---

## 7. 可以推迟

- **全屏包浏览器。** 行模式是默认；全屏浏览器与 CLI 形状冲突，如果最终要做，用辅助操作
  显式开启，绝不设为裸调用的默认行为。
- **查询分页器。** 直通命令的 stdout 直接交给终端；分页留给 shell 或 `less`，Pacpar 不接管。
- **事务评审 TUI（1.1）。** 1.0 用文本摘要，`-R` 直通；1.1 再替换确认提示，先覆盖
  `-Syu`，再覆盖 `-R` / `-Rs` 与清理陈旧依赖。
- **历史与撤销。** 这是生态里真正的空缺：libalpm 没有回滚，“撤销”在 Arch 上意味着从保留
  的归档中重装记录过的版本。F# 的 `TransactionReport` 是天然的账本记录，`-P` 形状的
  `history` / `undo` 扩展是与 pacman 形状兼容的落点。但归档与 PKGBUILD 的保留策略让
  这件事很重，最早也要放到 CLI 第二阶段。
- **终端主题探测。** 延迟探测属于打磨；初期 `NO_COLOR` 加 Spectre 默认值就够了。Spectre
  尊重 `NO_COLOR`，引擎本身不做背景色探测。
- **OSC 9;4 进度与响铃。** RazorConsole.Core 的 `AnsiSequences` 没有包装 OSC 9;4 与响铃。
  好用，但从不关键。

---

## 8. 待决问题

1. **裸调用**（3.3 节）：像 pacman 一样打印用法，还是像助手一样执行 `-Syu`？
2. **辅助操作还是纯长选项**：助手附加功能采用 `-P` 形状，还是更严格地只保留 pacman
   的选项集？
3. **危险选项子集**（3.2 节表格）：`--overwrite`、`-dd`、`--assume-installed`、
   `--ignore` 哪些 1.0 就提供，哪些刻意不做？
4. **组件归属**（5.3 节）：多选与进度条放在 RazorConsole.Elmish 层，还是作为 CLI 本地
   组件？引擎级会让未来所有 RazorConsole 应用受益，CLI 本地方案则能更快交付。
5. **AUR 评审粒度**：逐包 y/N 队列，还是一份合并的评审文档？两套都放在同一模型后面，
   试起来不贵。
6. **下载引擎默认值与配置键**（2.8.4 节）：`DownloadEngine` 与 `ConnectionsPerFile` 的
   默认值、允许范围，以及 `auto` 的判定标准。
7. **包到同步库的公开关联**（5.1 节）：新增 `PackageView.Database`，还是只暴露
   `AlpmSession` 级别的查询？前者直接服务评审与下载计划，后者更保守。
8. **`--ask` 的支持范围**：只支持需要交互的 question 类型，还是完整复刻 pacman 的
   问题掩码？前者足够 TUI，后者更兼容脚本。

---

## 附录：证据与索引

**Arch 一手资料：**

- [pacman (8)](https://man.archlinux.org/man/pacman.8.en)
- [libalpm_trans (3)](https://man.archlinux.org/man/libalpm_trans.3.en)
- [makepkg (8)](https://man.archlinux.org/man/makepkg.8.en)
- [pacman-key (8)](https://man.archlinux.org/man/pacman-key.8.en)
- [AUR](https://wiki.archlinux.org/title/Arch_User_Repository)
- [AUR helpers](https://wiki.archlinux.org/title/AUR_helpers)
- [System maintenance](https://wiki.archlinux.org/title/System_maintenance)

**pacman 源码中的对应位置：**

- `lib/libalpm/dload.c`：`_alpm_download`、`payload_download_fetchcb`、内部 curl 路径。
- `lib/libalpm/sync.c`：`compute_download_size`、`find_dl_candidates`、
  `download_files`、`_alpm_sync_load`。
- `lib/libalpm/be_local.c`：`alpm_pkg_set_reason`。
- `lib/libalpm/sandbox.c`：`alpm_sandbox_setup_child`。
- `src/pacman/conf.c`：`download_with_xfercommand`。
- `src/pacman/callback.c`：`--print` 下的 question 默认答案。
- `src/pacman/query.c`：`pacman_query`、`display`、`filter`、`-Qk` / `-Qkk`。
- `src/pacman/package.c`：`dump_pkg_full`、`dump_pkg_files`、`dump_pkg_changelog`。
- `src/pacman/files.c`：`pacman_files`、`files_search`、`files_list`、`print_owned_by`。
- `src/pacman/database.c`：`change_install_reason`、`-Dk` / `-Dkk`。

源码结论基于 pacman 7.x 的上述文件；本文不写行号，避免版本漂移后引用失效。

**本仓库当前状态：**

- `src/Pacpar.Alpm/COMPLETENESS.md`：libalpm 与 pacman API 覆盖账本。
- `src/Pacpar.Alpm.FSharp/DESIGN.md`：F# 层的设计文档（尚未实现）。
- `docs/adr/0003-stable-failure-codes-for-fsharp.md`：稳定失败码的决定。
- `docs/adr/0011-sessions-are-single-threaded.md`：libalpm 会话的线程约束。
- `docs/adr/0014-enum-placement-and-native-bound-values.md`：`FetchResult` 的枚举放置。
- `reports/open-defects.md`：已核实但未修复的缺陷。
- `reports/api-and-structure-review.md`：公共 API 与文件结构的未完成项。
