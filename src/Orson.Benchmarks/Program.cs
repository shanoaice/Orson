using BenchmarkDotNet.Running;
using Orson.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(WarmBulkBenchmarks).Assembly).Run(args);
