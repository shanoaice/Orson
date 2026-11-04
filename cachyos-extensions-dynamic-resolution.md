# CachyOS 特性扩展的动态解析实现方案

> **状态：** 架构调研与实现方案报告。
> **适用版本：** Orson.Alpm 1.0+ / .NET 10。
> **目标范围：** 为 CachyOS 下游定制版 pacman/libalpm 所引入的特有符号提供无损、跨平台优雅回退的动态绑定与托管封装。

---

## 1. 结论先行

在 CachyOS 环境中，`libalpm.so.16` 引入了两个未被上游 Arch Linux 合入的特有符号：
1. `alpm_option_get_physical_architectures`：检测 CPU 微架构层级（如 `x86_64_v3`）并返回物理支持的架构列表。
2. `alpm_pkg_get_installed_db`：查询已安装包的来源同步仓库数据库名称（如 `cachyos`、`core`）。

**核心建议：**
- **不可在编译期静态链接：** 严禁直接在 Rust bindgen 规则、`alpm.rs` 或静态 `[LibraryImport]` 中引入这两个符号，否则会导致上游 Arch Linux 镜像中的 CI 绑定漂移检查失败，或在运行时因符号缺失直接崩溃（`EntryPointNotFoundException`）。
- **采用动态函数指针（Unmanaged Function Pointers）：** 通过 `System.Runtime.InteropServices.NativeLibrary.TryGetExport` 结合 C# `delegate* unmanaged[Cdecl]` 惰性加载入口点。支持零托管委托分配开销、严格的 Cdecl 调用约定以及天然的能力探测（Capability Query）。
- **API 隔离与命名空间规范：** 建议将这两个特有功能放置于扩展命名空间或扩展类型（如 `Orson.Alpm.CachyOs` 或 `CachyOsPackageExtensions` / `CachyOsAlpmExtensions`），或者作为带有 `IsSupported` 标志的显式 `Try*` 方法，避免污染通用的通用 Arch Linux ALPM 抽象契约。

---

## 2. 符号语义与源码级逆向分析

通过对 CachyOS 维护团队的补丁仓库（`vnepogodin/my-patches`）以及本机 `/usr/lib/libalpm.so.16`、`/usr/bin/pacman` 二进制的反汇编分析，这两个函数的具体行为与内存契约如下：

### 2.1 `alpm_option_get_physical_architectures`

- **C 声明：**
  ```c
  alpm_list_t *alpm_option_get_physical_architectures(alpm_handle_t *handle);
  ```
- **上游来源：** CachyOS 补丁 `0001-add-support-for-x86_64_v-2-3-4-autodetection.patch`。
- **内部实现逻辑：**
  1. 调用 `uname()` 读取宿主系统的机器名称（`un.machine`，如 `x86_64`），并通过 `strdup` 复制。
  2. 通过汇编指令 `cpuid` 和 `xgetbv`，探测 CPU 标志位（`SSSE3`, `SSE4_1`, `SSE4_2`, `AVX`, `AVX2`, `AVX512*` 等）。
  3. 依次判定并 `strdup` 指令集层级字符串：
     - 若满足 v2，追加 `"x86_64_v2"`；
     - 若满足 v3，追加 `"x86_64_v3"`；
     - 若满足 v4，追加 `"x86_64_v4"`。
  4. 组装为 `alpm_list_t` 双向链表返回。
- **关键内存契约（Memory Contract）：**
  - 函数注释明确指出：`caller is responsible for alpm_list_free`。
  - **重要警示：** pacman 的 `conf.c` 中仅对该链表调用了 `alpm_list_free(physical_arches)`，这是因为 pacman 把节点内部通过 `strdup` 分配的字符串指针直接移交给了 `config->architectures`，随后由架构列表统一销毁。
  - **托管层生命周期责任：** 在 C# 中，如果作为只读查询接口调用，**必须释放每一个字符串和链表本身**。这正好与 Pacpar 现有的 `AlpmStringList.TakeOwned(rawList, &UnmanagedDestructors.CFreeExtern)` 契约 100% 吻合（通过 `alpm_list_free_inner` 配合 libc `free` 释放元素，再调用 `alpm_list_free` 释放节点）。

### 2.2 `alpm_pkg_get_installed_db`

- **C 声明：**
  ```c
  const char *alpm_pkg_get_installed_db(alpm_pkg_t *pkg);
  ```
- **上游来源：** CachyOS 补丁 `0001-add-information-about-origin-db.patch`。
- **内部实现逻辑：**
  1. pacman 在安装/升级包时，在本地数据库条目 `/var/lib/pacman/local/<pkg>/desc` 中写入 `%INSTALLED_DB%` 字段（记录当时所属的同步库名称）。
  2. `alpm_pkg_get_installed_db` 读取 `pkg->installed_db`（惰性加载 `INFRQ_DESC`）。
  3. 若包不是来自本地数据库，或者包在安装时未记录该信息（例如由官方未打补丁的 pacman 安装），返回 `NULL`。
- **关键内存契约（Memory Contract）：**
  - 返回指针指向 `alpm_pkg_t` 内部持有的字符串指针，由包自身持有，生命周期持续到 `_alpm_pkg_free`。
  - 托管层使用 `NativeString.FromNative((nint)ptr)` 复制为托管 `string?`，无需且不可主动 `free` 该原生指针。

---

## 3. 架构设计与实现策略

### 3.1 动态解析基础设施 (`NativeDynamicEntryPoints.cs`)

推荐在 `src/Orson.Alpm/Interop/` 或 `src/Orson.Alpm/bindings/` 目录下创建专用内部类，负责解析和持有可选的原生函数指针。

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Orson.Alpm.Bindings;

namespace Orson.Alpm.Interop;

internal static unsafe class CachyOsNativeMethods
{
  private static readonly delegate* unmanaged[Cdecl]<_alpm_handle_t*, _alpm_list_t*> s_getPhysicalArchitectures;
  private static readonly delegate* unmanaged[Cdecl]<_alpm_pkg_t*, byte*> s_getInstalledDb;

  static CachyOsNativeMethods()
  {
    // 复用 NativeMethods.__DllName ("libalpm.so.16")
    if (NativeLibrary.TryLoad("libalpm.so.16", typeof(CachyOsNativeMethods).Assembly, DllImportSearchPath.SystemAssembly, out var libHandle))
    {
      if (NativeLibrary.TryGetExport(libHandle, "alpm_option_get_physical_architectures", out var pPhysArch))
      {
        s_getPhysicalArchitectures = (delegate* unmanaged[Cdecl]<_alpm_handle_t*, _alpm_list_t*>)pPhysArch;
      }

      if (NativeLibrary.TryGetExport(libHandle, "alpm_pkg_get_installed_db", out var pInstalledDb))
      {
        s_getInstalledDb = (delegate* unmanaged[Cdecl]<_alpm_pkg_t*, byte*>)pInstalledDb;
      }
    }
  }

  internal static bool IsPhysicalArchitecturesSupported => s_getPhysicalArchitectures != null;

  internal static bool IsInstalledDatabaseSupported => s_getInstalledDb != null;

  internal static _alpm_list_t* alpm_option_get_physical_architectures(SafeAlpmHandle handle)
  {
    if (s_getPhysicalArchitectures == null)
    {
      throw new PlatformNotSupportedException("The installed libalpm does not export alpm_option_get_physical_architectures.");
    }

    var success = false;
    handle.DangerousAddRef(ref success);
    try
    {
      return s_getPhysicalArchitectures((_alpm_handle_t*)handle.DangerousGetHandle());
    }
    finally
    {
      if (success) handle.DangerousRelease();
    }
  }

  internal static byte* alpm_pkg_get_installed_db(_alpm_pkg_t* pkg)
  {
    if (s_getInstalledDb == null)
    {
      throw new PlatformNotSupportedException("The installed libalpm does not export alpm_pkg_get_installed_db.");
    }

    return s_getInstalledDb(pkg);
  }
}
```

### 3.2 托管外观 API 设计（二选一评估）

#### 选项 A：集成到现有类型（`AlpmOptions` 与 `Package`）
- 在 `AlpmOptions` 增加：
  ```csharp
  public static bool IsPhysicalArchitecturesSupported => CachyOsNativeMethods.IsPhysicalArchitecturesSupported;

  public IReadOnlyList<string> GetPhysicalArchitectures()
  {
    ThrowIfDisposed();
    var list = CachyOsNativeMethods.alpm_option_get_physical_architectures(_handle);
    GC.KeepAlive(this);
    return AlpmStringList.TakeOwned(list, &UnmanagedDestructors.CFreeExtern);
  }
  ```
- 在 `Package` / `PackageView` 增加：
  ```csharp
  public static bool IsInstalledDatabaseSupported => CachyOsNativeMethods.IsInstalledDatabaseSupported;

  public string? InstalledDatabase
  {
    get
    {
      ThrowIfDisposed();
      if (!IsInstalledDatabaseSupported) return null;
      var ptr = CachyOsNativeMethods.alpm_pkg_get_installed_db(BackingStruct);
      GC.KeepAlive(this);
      return NativeString.FromNative((nint)ptr);
    }
  }
  ```
- **优缺点：** 调用最自然，开箱即用；但在官方 Arch Linux 下属性为 null 或方法抛异常。

#### 选项 B：专用扩展命名空间（`Orson.Alpm.CachyOs`，推荐）
- 通过扩展方法或独立外观类提供扩展：
  ```csharp
  namespace Orson.Alpm.CachyOs;

  public static class CachyOsExtensions
  {
    public static bool IsCachyOsDistribution =>
      CachyOsNativeMethods.IsPhysicalArchitecturesSupported || CachyOsNativeMethods.IsInstalledDatabaseSupported;

    public static bool TryGetPhysicalArchitectures(this AlpmOptions options, out IReadOnlyList<string> architectures)
    {
      if (!CachyOsNativeMethods.IsPhysicalArchitecturesSupported)
      {
        architectures = [];
        return false;
      }

      var list = CachyOsNativeMethods.alpm_option_get_physical_architectures(options.Handle);
      architectures = AlpmStringList.TakeOwned(list, &UnmanagedDestructors.CFreeExtern);
      return true;
    }

    public static string? GetInstalledDatabase(this Package package)
    {
      if (!CachyOsNativeMethods.IsInstalledDatabaseSupported) return null;
      var ptr = CachyOsNativeMethods.alpm_pkg_get_installed_db(package.BackingStruct);
      return NativeString.FromNative((nint)ptr);
    }
  }
  ```
- **优缺点：**
  1. 架构界限极为清晰：下游发行版特有机制完全收敛在 `Orson.Alpm.CachyOs`。
  2. 核心通用层 `Orson.Alpm` 保持标准 libalpm 的纯洁契约。
  3. 调用方意图明确：明确知道自己在消费 CachyOS 特性，且使用 `Try*` 模式不需要处理非预期异常。

---

## 4. 内存安全性与生命周期保障

1. **`physical_architectures` 链表销毁：**
   - 原生函数每调用一次，都会 `malloc` 若干链表节点和通过 `strdup` 分配架构名字符串。
   - 托管层必须调用 `AlpmStringList.TakeOwned(list, &UnmanagedDestructors.CFreeExtern)`，遍历构建托管 `string[]` 后，通过 `alpm_list_free_inner` 逐一 `free` 字符串，最后 `alpm_list_free` 释放链表节点。
   - 该过程不存在句柄外泄，亦无原生内存泄漏风险。
2. **`installed_db` 字符串指针有效性：**
   - 该指针生命周期与底层 `alpm_pkg_t` 绑定，在读取完成并被 `NativeString.FromNative` 转换为不可变托管 `System.String` 之前，必须使用 `GC.KeepAlive(this)` 确保拥有该包句柄的对象不会被垃圾收集。
3. **安全句柄（SafeHandle）保护：**
   - 调用 `alpm_option_get_physical_architectures` 时，必须使用 `DangerousAddRef` / `DangerousRelease` 保护 `SafeAlpmHandle`，防止会话在多线程并发关闭时被释放。

---

## 5. 自动化测试与 CI 验证方案

由于 CI 环境（官方 `archlinux:base-devel`）与开发机（CachyOS）环境的二进制不一致，测试策略必须双向覆盖：

1. **能力嗅探测试（Capability Tests）：**
   ```csharp
   [Fact]
   public void PhysicalArchitectures_AvailabilityMatchesEnvironment()
   {
     if (CachyOsNativeMethods.IsPhysicalArchitecturesSupported)
     {
       using var env = new IsolatedAlpmEnvironment();
       var arches = env.AlpmSession.Options.GetPhysicalArchitectures();
       Assert.NotEmpty(arches);
       Assert.Contains("x86_64", arches);
     }
     else
     {
       using var env = new IsolatedAlpmEnvironment();
       Assert.Throws<PlatformNotSupportedException>(() => env.AlpmSession.Options.GetPhysicalArchitectures());
     }
   }
   ```
2. **CI 一致性保障：**
   - 在 CI 容器中，动态解析失败，测试将执行 `else` 分支断言 `PlatformNotSupportedException`（或 `Try*` 返回 `false`），测试 100% 通过。
   - 在 CachyOS 本机环境中，测试自动执行 `if` 分支并验证微架构检测结果（如 `x86_64_v3`）。
   - 两者皆无需任何条件编译宏（`#if`），实现纯运行时自适应。

---

## 6. 决策建议与后续行动

| 维度 | 建议方案 | 理由 |
| :--- | :--- | :--- |
| **P/Invoke 方式** | 动态 `NativeLibrary.TryGetExport` + `delegate* unmanaged[Cdecl]` | 零分配、零开销、不污染静态绑定生成器，免去 CI 符号漂移冲突 |
| **API 形状** | `Orson.Alpm.CachyOs` 扩展方法 (`TryGetPhysicalArchitectures`, `GetInstalledDatabase`) | 明确发行版语义边界，核心契约不产生隐式平台依赖 |
| **内存释放** | 复用 `AlpmStringList.TakeOwned` + `UnmanagedDestructors.CFreeExtern` | 现成成熟的基础设施，严密释放 `strdup` 元素与链表骨架 |
| **文档归档** | 将状态更新记录在 `COMPLETENESS.md` 扩展说明段落 | 清晰记录每个特性的实现层级与所属发行版来源 |
