# heaptrack 用于 native 泄漏测试的评估

> 结论先行：**不建议把 heaptrack 当作整个 .NET/xUnit 进程中 `mallinfo2` 的透明替代品，也不建议对整套测试设置 `total memory leaked == 0` 门禁。**它适合补充精确的分配点诊断；只有在独立、受控的 native probe 中，并确认目标分配器确实被拦截后，才可考虑以退出时未释放字节数替代堆增长检查。

## 范围与依据

仅查阅 KDE 官方仓库 `KDE/heaptrack`，固定到 2026-11-16 的 master 提交 [`9bb0de68e8e37beca7d3f3feabc8f57395de4490`](https://github.com/KDE/heaptrack/tree/9bb0de68e8e37beca7d3f3feabc8f57395de4490)。下文将**事实**与**推论/建议**分开；未运行 Orson 测试或 heaptrack。

## 已核实的事实

### 1. 记录内容与 `leaked` 的精确含义

- 启动模式通过 `LD_PRELOAD` 记录核心分配函数调用及 backtrace；当前实现挂钩 `malloc`、`free`、`calloc`、`realloc`、`posix_memalign`，并按构建条件挂钩 `cfree`、`valloc`、`aligned_alloc`，另有 mimalloc 与 Boehm GC 的可选挂钩。每次成功分配记录大小、指针和 native trace，释放记录指针。[H][P][L]
- `leaked` 是**分析输入结束时尚未由匹配释放事件扣减的请求字节数**：分配时 `totalCost.leaked += size`，成功匹配释放时减去原大小；CLI 自己也定义为 `bytes not deallocated at the end`。[A][PR]
- 因而仍被全局指针、缓存或运行时引用、在退出时仍可达（still-reachable）的块，只要没释放，也计入 `leaked`。这里不是 Valgrind 式的“不可达泄漏”分类；实现只重放分配/释放事件，没有可达性扫描。[A]
- `heaptrack stats` 的 `leaked allocations` 是分配事件计数，而 `heaptrack_print` 末尾的 `total memory leaked` 是字节数，二者不可混用。[I][PR]
- 直接 `mmap`、绕过 `malloc` 家族的 pool/custom allocator 不会自动成为逐块 heap 事件。官方 README 明确说这要求修改内存池实现；公开 API 是 `heaptrack_report_alloc`、`heaptrack_report_realloc`、`heaptrack_report_free`。[R][API]
- 正常启动模式的 `atexit` 关闭路径刻意保留追踪数据以收集 static deallocation；因此“退出时”包含该追踪器正常关闭前观察到的事件，而不是任意测试方法结束时的快照。[L]

### 2. 不是内存访问正确性检查器

- heaptrack 的启动器与分配钩子只拦截分配/释放并写事件；没有插桩普通内存读写。因此它**不检测 invalid read/write 或 use-after-free**。[H][P]
- `free` 钩子先写释放事件，再调用真实 `free`；解释器遇到找不到对应分配的指针时直接忽略该释放事件。正常输出没有“double free  detected”诊断，故也不能把 heaptrack 当作 double-free 检查器；底层分配器自身是否报错是另一回事。[P][I]

### 3. CI 可用的真实 CLI 行为

- 记录端有 `--record-only`、`-o/--output`、`--unwind-depth DEPTH`（1–64）、`--follow-children` 等选项；指定输出 `BASE` 后，实际文件为 `BASE.gz` 或 `BASE.zst`，取决于构建及 zstd 可用性。启动器最终返回被测进程退出码。[H]
- `heaptrack_print --print-leaks PROFILE`（短选项 `-l`）增加 `MEMORY LEAKS` 段；无论是否启用该段，末尾都会打印 `total memory leaked:`。默认热点数量由 `--peak-limit` 限制，所以“泄漏段没有列出某项”不等于总泄漏为零。[PR]
- `heaptrack_print --diff BASELINE PROFILE` 计算 `PROFILE - BASELINE` 的分配成本差值；它不是阈值断言，也不会仅因泄漏非零而返回非零。正常打印路径最后固定 `return 0`，所以 CI 必须另行解析结果。[A][PR]
- `--filter-bt-function FUNCTION` 只保留 backtrace 中含指定函数的**打印条目**；`totalCost` 不在该过滤步骤重算，末尾 `total memory leaked` 仍是整个 profile 的值。xUnit 的测试过滤与这个选项也不能互相替代。[PR]
- `--suppressions FILE` 接受 LSAN 风格、每行以 `leak:` 开始的规则；内嵌及内建 suppression 默认启用，可分别用 `--disable-embedded-suppressions`、`--disable-builtin-suppressions` 关闭。匹配项会从 `total memory leaked` 扣除，并另列为 `suppressed leaks`；`--print-suppressions` 可显示命中统计。[PR][A][S]
- 另有 `--print-massif FILE` 与 `--print-flamegraph FILE`，但它们是 Massif/collapsed-stack 转换，不是专门的 CI 阈值协议。[PR]
- README 将 `heaptrack_print` 定义为人类可读的 ASCII 报告；在上述官方 README 和 CLI 源码中，未见输出文本稳定性、JSON 或机器可读 CI 接口承诺。若解析文本，应固定 heaptrack 版本并做解析失败即失败的包装，而不能假设跨版本格式不变。[R][PR]

### 4. .NET 支持边界

- 本次查阅的官方 README 和相关实现没有 .NET、CoreCLR、Mono 或 managed-stack 支持文档/承诺；这是一项**第一方资料缺失**，不是官方宣称“不支持 .NET”。[R][T][I]
- 已实现的 trace 是 libunwind/native unwind-table 得到的指令地址，再由 ELF 符号与 DWARF 信息解析模块、函数、源码行；没有 managed 方法名或 logical async stack 的解析路径。[T][I]

## 推论与落地建议

### 整个 .NET 测试套件

1. **运行时噪声会进入同一个总数。**只要 CoreCLR、testhost、xUnit 或 native 库经由已挂钩 API 分配且退出前未释放，就会进入 `total memory leaked`；这与目标链表是否泄漏无关。仍可达缓存也计入，因此零总量很容易产生误报。[P][A]
2. **native 调用可见不等于 C# 栈可还原。**可期待的是（在 unwind 与符号可用时）libalpm/glibc 等 native 帧；不能依据官方资料期待 C# 方法名、托管逻辑栈或 JIT 帧的可靠符号化。[T][I]
3. **总泄漏为零也不证明目标无泄漏。**custom allocator/mmap 可能完全漏记；suppression 又会有意从总数扣除匹配项。它还不能发现 UAF、越界访问或 double free。[API][A][I]

### 专用、过滤后的 xUnit 进程

- 用 xUnit filter 单独启动进程能缩小测试体范围，但不会消除 CLR/testhost 的启动、JIT、缓存与关闭期分配；heaptrack 的 `--filter-bt-function` 又不会过滤末尾总数。因此它适合生成较易阅读的 `MEMORY LEAKS` 报告，**仍不适合零总量门禁**。[PR]
- 可以保留常规功能/所有权测试，把 heaptrack 报告作为失败诊断产物；若采用 `--diff`，应先固定同样的启动、warm-up、运行时与 heaptrack 版本，并审查差分栈，而不是把差分非零直接等同回归。[A][PR]

### 独立 native probe（若必须替代 `mallinfo2`）

- 这是更可控的方案：最小程序只执行目标 libalpm/list 分配与释放后正常退出；先用正、反例验证目标分配确实出现在 profile 中，再检查目标栈和未释放字节。这样才能避免把 CLR 生命周期噪声混入门禁。[P][PR]
- 即便在 native probe 中，`total memory leaked == 0` 也只表示“**heaptrack 看见且未被 suppression 排除的分配均已匹配释放**”。必须固定工具版本、审阅内建/自定义 suppression、保存原始 profile，并另外使用内存错误检查手段覆盖 UAF/越界/double-free。[A][S][I]

## 最终建议

| 用途 | 建议 |
|---|---|
| 替换整套 xUnit 中的 `mallinfo2` 堆增长测试 | **不直接替换**；语义和进程范围不同。 |
| 独立 native probe 的退出泄漏检查 | **可有条件采用**，先验证分配器覆盖与 suppression 基线。 |
| 整套 .NET 测试的 `zero-total-leaks` CI 门禁 | **不安全，不推荐**。 |
| 分配热点与泄漏栈诊断 | **推荐作为补充**，保存 `heaptrack_print --print-leaks` 输出及原始 profile。 |

[H]: https://github.com/KDE/heaptrack/blob/9bb0de68e8e37beca7d3f3feabc8f57395de4490/src/track/heaptrack.sh.cmake#L10-L78
[P]: https://github.com/KDE/heaptrack/blob/9bb0de68e8e37beca7d3f3feabc8f57395de4490/src/track/heaptrack_preload.cpp#L98-L120
[L]: https://github.com/KDE/heaptrack/blob/9bb0de68e8e37beca7d3f3feabc8f57395de4490/src/track/libheaptrack.cpp#L1077-L1105
[A]: https://github.com/KDE/heaptrack/blob/9bb0de68e8e37beca7d3f3feabc8f57395de4490/src/analyze/accumulatedtracedata.cpp#L339-L394
[PR]: https://github.com/KDE/heaptrack/blob/9bb0de68e8e37beca7d3f3feabc8f57395de4490/src/analyze/print/heaptrack_print.cpp#L556-L620
[I]: https://github.com/KDE/heaptrack/blob/9bb0de68e8e37beca7d3f3feabc8f57395de4490/src/interpret/heaptrack_interpret.cpp#L706-L740
[R]: https://github.com/KDE/heaptrack/blob/9bb0de68e8e37beca7d3f3feabc8f57395de4490/README.md#L218-L274
[API]: https://github.com/KDE/heaptrack/blob/9bb0de68e8e37beca7d3f3feabc8f57395de4490/src/track/heaptrack_api.h#L8-L24
[T]: https://github.com/KDE/heaptrack/blob/9bb0de68e8e37beca7d3f3feabc8f57395de4490/src/track/trace_libunwind.cpp#L55-L70
[S]: https://github.com/KDE/heaptrack/blob/9bb0de68e8e37beca7d3f3feabc8f57395de4490/src/analyze/suppressions.cpp#L72-L121
