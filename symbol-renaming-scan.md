# Pacpar.Alpm 符号命名扫描与重命名规划报告

---
date: 2026-11-03
status: completed
scope: Pacpar.Alpm
---

## 1. 概述与背景

在前期重构中，`AlpmNativeList` 被提取并重命名为 `NativeListMarshal`（位于 `src/Pacpar.Alpm/Interop/NativeListMarshal.cs`）。该重构的核心价值在于**修正抽象与职责错位**：该类型并非集合容器，而是跨 P/Invoke 边界编排 `_alpm_list_t*` 的封送工具类。

本报告对 `Pacpar.Alpm` 项目展开全量符号扫描，识别相似的命名缺陷，并评估前缀调整对命名空间冲突的潜在影响，以作为后续多阶段任务的执行依据。

---

## 2. 命名空间冲突与前缀安全性评估

盲目剔除类型名称中的 `Alpm` 前缀会引发严重的命名空间冲突或歧义。根据编译依赖与框架设计规范（FDG），评定边界如下：

| 符号 | 现状评估 | 处理决策 |
|---|---|---|
| `AlpmList<T>` | 若更名为 `List<T>`，当外部消费方同时声明 `using Pacpar.Alpm;` 与 `using System.Collections.Generic;` 时，所有 `List<T>` 引用均会触发 `CS0104: 'List<T>' is an ambiguous reference` 编译中断。 | **必须保留** |
| `AlpmException` | 若更名为 `Exception`，直接与根异常 `System.Exception` 冲突。 | **必须保留** |
| `AlpmEvent` | 若更名为 `Event`，不仅与 C# 关键字 `event` 冲突（需使用 `@event`），还会与 `System.Diagnostics.Tracing.Event` 冲突。 | **必须保留** |
| `AlpmSession` | 标识底层 libalpm 实例的宿主会话，保留前缀可避免与常见网络会话、诊断会话等通用名词混淆。 | **建议保留** |
| `AlpmOptions` | 若更名为 `Options`，极易与 `Microsoft.Extensions.Options.Options` 产生歧义。 | **建议保留** |
| `AlpmOptionList<T>` | `internal` 抽象类，更名为 `OptionList<T>` 仅在组件内部可见，无外部破坏性。 | **可选重命名**（优先级低） |

---

## 3. 扫描发现与重命名清单

### 3.1 职责与概念错位（类似 `AlpmNativeList -> NativeListMarshal`）

此类符号名称暗示了某种抽象角色，但内部实际行为与名称不符：

| 现有符号 | 类型与位置 | 现状与问题分析 | 建议命名 | 涉及文件 |
|---|---|---|---|---|
| `NativeCall` | `internal static class`<br>`src/Pacpar.Alpm/Interop/NativeCall.cs` | 命名暗示其为原生调用执行器（形如 `NativeCall.Invoke`），但内部不执行任何原生调用逻辑。其唯一成员为 `Failure(...)`，专用于读取 errno 并组装 `AlpmFailure`。 | `NativeFailure` 或 `AlpmFailureFactory`<br>（或将方法内聚并入 `AlpmFailure.Of`） | `NativeCall.cs`, `AlpmSession.cs`, `Database.cs`, `Transaction.cs`, `PackageBase.cs` 等 10 余处调用点 |
| `MemoryManagement` | `internal static class`<br>`src/Pacpar.Alpm/Interop/MemoryManagement.cs` | 命名过于宽泛。其实际职责仅为提供传递给 `alpm_list_free_inner` 的原生析构回调桩（`CFreeExtern`, `DepMissingFreeExtern`, `ConflictFreeExtern`, `FileConflictFreeExtern`）与 libc `free`。且 `UnmanagedFreeExtern` 为无调用点的废弃代码。 | `NativeDestructors` 或 `UnmanagedDestructors`<br>（同步清理无用方法） | `MemoryManagement.cs`, `AlpmSession.Seams.cs`, `AlpmFailure.cs`, `ListOwnershipTests.cs` |
| `ErrorHandler` | `internal static class`<br>`src/Pacpar.Alpm/AlpmFailure.cs` | 并不执行任何错误拦截或生命周期处理操作，实质是将原生 errno 映射为托管异常、错误描述字符串（`alpm_strerror`）、错误分类与语义错误码的工厂工具。 | `AlpmErrorTranslator` 或 `ErrnoMapper` | `AlpmFailure.cs`, `AlpmSession.cs`, `Exceptions.cs`, `ErrorHandlerTests.cs` |

### 3.2 C 语言缩写泄露与风格分裂

此类符号暴露了原生 C 接口的内部缩写，且在同类定义中产生了命名风格不一致：

| 现有符号 | 类型与位置 | 现状与问题分析 | 建议命名 | 涉及文件 |
|---|---|---|---|---|
| `ConflictPkg` | `public sealed class`<br>`src/Pacpar.Alpm/Events/AlpmQuestion.cs` | 在 `AlpmQuestion` 内部出现分裂：`InstallIgnoredPackage` 和 `ReplacePackage` 拼写了完整单词 `Package`，而该类直接保留了 C 结构体宏缩写 `Pkg`。 | `ConflictPackage` 或 `PackageConflict` | `AlpmQuestion.cs`, `ManagedSnapshotTests.cs` |
| `CorruptedPkg` | `public sealed class`<br>`src/Pacpar.Alpm/Events/AlpmQuestion.cs` | 同上，残留 C 缩写 `Pkg`。 | `CorruptedPackage` | `AlpmQuestion.cs` |
| `RemovePkgs` | `public sealed class`<br>`src/Pacpar.Alpm/Events/AlpmQuestion.cs` | 同上，残留 C 复数缩写 `Pkgs`。 | `RemovePackages` | `AlpmQuestion.cs`, `ManagedSnapshotTests.cs` |
| `FileConflict.Ctarget` | `public string?` 属性<br>`src/Pacpar.Alpm/Conflicts.cs` | 直接沿用 C 结构体字段 `ctarget`，含义为产生冲突的目标包名，首字母 `C` 缩写晦涩且违反 C# 命名规范。 | `ConflictingTarget` 或 `ConflictingPackage` | `Conflicts.cs` |
| `PackageBase.Arch` | `public string?` 属性<br>`src/Pacpar.Alpm/Package/PackageBase.cs` | 对应原生 `alpm_pkg_get_arch`。与 `AlpmOptions.Architectures` 概念不一致，违背 FDG 避免通用缩写的原则。 | `Architecture` | `PackageBase.cs`, `PackageSnapshot.cs` |
| `DepMissing` | `public sealed class`<br>`src/Pacpar.Alpm/Dependencies.cs` | 直接沿用原生 `_alpm_depmissing_t`。应为完整名词短语；其属性 `CausingPkg` 缩写了 `Pkg`，属性 `Depend` 使用了动词。 | `MissingDependency`；属性调整为 `CausingPackage` 与 `Dependency` | `Dependencies.cs`, `AlpmSession.cs`, `Exceptions.cs`, `AlpmFailure.cs`, `gc-anchor-allowlist.txt` |
| `Depend` | `public sealed class`<br>`src/Pacpar.Alpm/Dependencies.cs` | 类名使用动词 `Depend`，实际表示单个依赖规则项（名词 `Dependency`）。 | `Dependency`（与文件名 `Dependencies.cs` 及语义对齐） | `Dependencies.cs`, `AlpmOptions.cs`, `PackageBase.cs`, `gc-anchor-allowlist.txt` 等 |
| `DepMod` | `public enum`<br>`src/Pacpar.Alpm/Dependencies.cs` | 对应 `alpm_depmod_t`，类名缩写，其属性名 `Depmod` 大小写不规范。 | `DependencyModifier` 或 `DependencyComparison` | `Dependencies.cs`, `PackageBase.cs` |

### 3.3 概念单复数与实体映射错位

| 现有符号 | 类型与位置 | 现状与问题分析 | 建议命名 | 涉及文件 |
|---|---|---|---|---|
| `Backup` | `public sealed class`<br>`src/Pacpar.Alpm/Backup.cs` | 类型名为动作/抽象名词 `Backup`。但在 `PackageBase.cs` 中属性为 `public AlpmList<Backup> BackupFiles { get; }`。每个元素实质为一条具体备份文件记录（对应 `alpm_backup_t`，含文件路径 `Name` 与 MD5 `Hash`）。对照 `PackageFile`，此处命名粒度错位。 | `BackupFile` 或 `BackupEntry` | `Backup.cs`, `PackageBase.cs`, `PackageSnapshot.cs`, `gc-anchor-allowlist.txt` |
| `Architecture` | `internal sealed class`<br>`src/Pacpar.Alpm/Options/Architecture.cs` | 类名为单数 `Architecture`，但继承自 `AlpmStringOptionList`，用来支持 `AlpmOptions.Architectures` 架构集合。`Options/` 目录下其余集合类均为复数（`CacheDirectories`、`HookDirectories`、`IgnoreGroups`、`IgnorePackages`、`OverwritableFiles`）。单数名称容易误导调用者以为是单架构枚举或结构。 | `Architectures` 或 `ArchitecturesOption` | `Options/Architecture.cs`, `AlpmOptions.cs` |
| `PackageBase` | `public abstract class`<br>`src/Pacpar.Alpm/Package/PackageBase.cs` | 公有 API 暴露了 `PackageBase`，但库中不存在名为 `Package` 的实体（外部通过 `PackageView` 或 `LoadedPackage` 访问）。FDG 规范指出：在无需暴露抽象基类继承树给外部扩展时，避免使用 `Base` 后缀。 | `Package` | `PackageBase.cs`, `PackageView.cs`, `LoadedPackage.cs`, `Exceptions.cs`, `gc-anchor-allowlist.txt` 等 |

### 3.4 方法名冗余与实现遗留参数

| 现有符号 | 位置 | 现状与问题分析 | 建议命名 | 涉及文件 |
|---|---|---|---|---|
| `Group.FindGroupPackages(dbs)` | `public IReadOnlyList<PackageView>`<br>`src/Pacpar.Alpm/Groups.cs` | 在 `Group` 实例上调用 `group.FindGroupPackages(dbs)`，方法名重复了类型前缀 `Group`（源自 C 的 `alpm_find_group_pkgs`）。 | `group.FindPackages(dbs)` | `Groups.cs`, `LifetimeInvalidationTests.cs`, `gc-anchor-allowlist.txt` |
| `Transaction._library` / `alpmLibrary` | 字段与构造参数<br>`src/Pacpar.Alpm/Transaction.cs` | 早期 `AlpmSession` 曾命名为 `AlpmLibrary` 或 `Alpm`，导致 `Transaction` 内依然保留字段 `_library` 与形参 `alpmLibrary`。 | `_session` / `session` | `Transaction.cs`, `Transaction.Seams.cs` |

---

## 4. 实施阶段规划与依赖排期

为保证构建稳定性，并防范改动波及基线测试（特别是 `tests/Pacpar.Alpm.Tests/Unit/GcAnchor/gc-anchor-allowlist.txt` 对类型和成员名称的强校验），建议按以下批次分步实施：

### 阶段 1：纯内部工具类与内部字段重命名（零公开 API 破坏）
- [x] 重命名 `NativeCall` -> 内聚并入 `AlpmFailure.Of`
- [x] 重命名 `MemoryManagement` -> `UnmanagedDestructors`，移除死代码 `UnmanagedFreeExtern`
- [x] 重命名 `Architecture` (Options 集合类) -> `Architectures`
- [x] 重构 `Transaction` 中的 `_library` 字段与 `alpmLibrary` 参数为 `_session` 与 `alpmSession`
- [x] 验证：运行 `dotnet test`，确保无内部编译或运行时断言故障

### 阶段 2：公开 API 缩写纠正与一致性统一（小范围破坏）
- [x] 统一 `AlpmQuestion` 子类名称：
  - `ConflictPkg` -> `PackageConflict`
  - `CorruptedPkg` -> `CorruptedPackage`
  - `RemovePkgs` -> `RemovePackages`
- [x] 重命名 `Backup` -> `BackupEntry`
- [x] 重命名属性 `FileConflict.Ctarget` -> `ConflictingTarget`
- [x] 重命名属性 `PackageBase.Arch` -> `Architecture`
- [x] 简化方法 `Group.FindGroupPackages` -> `Group.FindPackages`
- [x] 同步维护 `tests/Pacpar.Alpm.Tests/Unit/GcAnchor/gc-anchor-allowlist.txt` 中受影响的条目（`BackupEntry`, `Group.FindPackages` 等）
- [x] 验证：运行完整单元测试套件，检查 `PublicApiSurfaceTests` 与 `GcAnchorAuditTests`

### 阶段 3：核心依赖领域模型重构（广泛破坏）
- [x] 重命名 `DepMissing` -> `MissingDependency`，同步更新 `CausingPkg` -> `CausingPackage` 与 `Depend` -> `Dependency`
- [x] 重命名 `Depend` -> `Dependency`
- [x] 重命名 `DepMod` -> `DependencyComparison`
- [x] 将 `PackageBase` 简化为 `Package`
- [x] 同步更新 ADR 文档、DocFX API 目录、F# 互操作映射文档及基准测试代码
- [x] 验证：全方案构建与回归测试（320/320 测试通过，DocFX 0 告警 0 错误）
