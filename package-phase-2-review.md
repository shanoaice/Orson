# Package 架构 Phase 2 重构复核报告

- **当前分支/变更**：已拆分为两个原子提交：
  1. `rpvttnuv` (`perf(pkg): implement small buffer optimization for PackageVersion`)
  2. `zlovuzro` (`feat(pkg)!: implement zero-allocation package architecture (phase 2)`)
- **审计日期**：2026-11-15
- **验证状态**：Debug 与 Release 全部 342 项测试通过，编译警告 0，PublicAPI 校验通过。
- **最新收敛**：移除了未被引用的 `PackageView(_alpm_pkg_t*, LifetimeStamp)` 冗余构造重载，收紧为唯一接受 `ChildLifetime` 的构造入口，在编译期杜绝了根生命周期逃逸风险。

---

## 1. 核心架构变更总结

本变更落实了零分配包元数据体系的第二阶段目标：

1. **废除 `Package` 抽象基类，引入 40 字节值类型 `PackageView`**：
   - 布局：`_alpm_pkg_t* _rawPackage`（8 字节） + `LifetimeStamp _stamp`（32 字节，包含两级 Lifetime 引用与代际计数）。
   - 内存模型：直接借用 libalpm 堆内存，彻底消除每个包实体的堆对象分配开销。
   - 提供 `NameUtf8` 和 `VersionUtf8` 等原生 UTF-8 `ReadOnlySpan<byte>` 切片，避免转码与字符串分配。
2. **抽象通用契约 `IPackageMetadata`**：
   - `PackageView`、`LoadedPackage`、`PackageSnapshot` 统一实现该接口，消除泛型与多态调用下的类型歧义。
3. **`PackageVersion` 引入小缓冲区优化（SBO）**：
   - 内置 31 字节 `InlineVersionBuffer`（`[InlineArray(31)]`），长度小于 31 字节的 UTF-8 版本字符串完全内联。
   - 比较器 `CompareTo` 直接在内联栈内存或堆缓冲区中追加 `\0` 后传给 `alpm_pkg_vercmp`，完全消除比对期间的 `Utf8Buffer` 堆分配。
4. **`LoadedPackage` 职责明确化**：
   - 从原先继承 `Package` 改为组合 `PackageView`，作为归档文件包的原生所有权持有者。
   - 严格维护 `Disown()`（转交事务时释放管理但不销毁原生内存）与 `Dispose()`（注销并释放原生内存）的状态机。

---

## 2. 关键代码审查点与分析

### 2.1 `PackageView` 生命周期与内存安全
- **状态校验**：所有只读属性与原生调用均通过 `ThrowIfStale()` 校验代际与空指针。当所属的数据库、事务或会话失效时，精确抛出带有上下文描述的 `AlpmLifetimeException`。
- **GC 锚定（GCAnchor）**：
  - `_stamp` 字段提升了 `PackageView` 的所有权分类（`OwnershipClass.Strong`）。
  - `GetSignature`、`TryCheckMd5Sum` 以及 `OpenChangelogStream` 已将原生句柄求值移至必要分支或紧凑化调用，使 Debug 与 Release 模式下的 IL 回溯均稳定匹配接收者存活规则，白名单站点数稳定为 7。

### 2.2 `PackageVersion` 零结尾与跨边界比对
- `NullTerminatedSpan` 保证了内联和堆分配模式下尾部均显式存在 `\0` 字节。
- 处理了空版本（`s1.Length <= 1`）的边界情况，避免向原生代码传入未终结的指针。
- 完整实现了 `IUtf8SpanFormattable` 与 `ISpanFormattable`，无缝支持高性能格式化。

### 2.3 异常契约与事务传递
- `Transaction.TryAddPackage` 与 `Transaction.TryRemovePackage` 对值类型默认值 `pkg == default` 显式抛出 `ArgumentException`，阻断未初始化视图进入 libalpm 内部。
- `AlpmPackageException` 构造签名平滑迁移至 `IPackageMetadata? package`，兼容上层异常处理。

---

## 3. 建议核对的细节与需关注项

在最终提交或合并前，建议人工快速核验以下几处细节：

1. **未跟踪文件 `skills/system/run-workflow/SKILL.md`**：
   - 当前工作区中新增了该文件。请核对该文件是否属于本批次提交范畴，若为外部辅助工具误入，可在提交前将其移除或另行提交。
2. **`PackageView.ToString()` 在未初始化时的行为**：
   - 目前 `PackageView.ToString()` 直接返回 `Name`。若 `PackageView` 为 `default`（未初始化状态），调用 `ToString()` 会抛出 `InvalidOperationException("The PackageView is uninitialized.")`。
   - 对比之下，`PackageVersion.ToString()` 对 `default` 返回 `string.Empty`。此行为对于借用原生指针的结构体是符合防御性设计的，但需注意在日志打桩或调试器显示时避免对未初始化的结构体盲调 `ToString()`。
3. **`gc-anchor-allowlist.txt` 文本清理**：
   - 已将 `PackageView` 计数由 10 更新为 7，并将描述中已废除的 `Package instance` 说明更新为 `PackageView instance`。
4. **`PublicAPI.Unshipped.txt` 变更**：
   - 48 项旧 `Package` API 正确标记为 `*REMOVED*`，`PackageView` 与 `LoadedPackage` 的新导出 API 均已完整补录，无缺失符号。

---

## 4. 验证命令记录

```bash
# 1. 验证 Release 构建与 PublicAPI
dotnet build -c Release

# 2. 验证 Release 单元测试（CI 标准命令）
dotnet test --solution Orson.slnx -c Release --no-build

# 3. 验证 Debug 模式下的测试一致性与 GC 锚定审计
dotnet test --solution Orson.slnx -c Debug
```
全部命令均以退出代码 0 通过。
