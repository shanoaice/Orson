/*
 * Benchmark-only harness for the real alpm_cb_log va_list forwarding shape.
 *
 * BenchmarkDotNet cannot materialise a C va_list from managed code. This fixed-arity entry
 * point builds one at a variadic call site and passes it to the managed callback exactly as
 * libalpm does.
 */
#include <stdarg.h>

#define ORSON_BENCH_API __attribute__((visibility("default")))

typedef void (*orson_bench_logcb)(void *ctx, int level, const char *fmt, va_list ap);

static void orson_bench_emit(orson_bench_logcb cb, void *ctx, int level, const char *fmt, ...)
{
  va_list ap;
  va_start(ap, fmt);
  cb(ctx, level, fmt, ap);
  va_end(ap);
}

ORSON_BENCH_API void orson_bench_invoke_log(orson_bench_logcb cb, void *ctx, int level,
                                            const char *fmt, const char *text, int number)
{
  orson_bench_emit(cb, ctx, level, fmt, text, number);
}
