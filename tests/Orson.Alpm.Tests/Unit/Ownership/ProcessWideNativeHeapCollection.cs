namespace Orson.Alpm.Tests.Unit.Ownership;

/// <summary>Groups tests that measure process-wide native heap state.</summary>
/// <remarks>
/// xUnit runs the collections with <see cref="CollectionDefinitionAttribute.DisableParallelization" /> set after
/// all parallel collections have finished, so a test in this collection can measure the process heap without another test
/// allocating into the measurement window.
/// </remarks>
[CollectionDefinition("ProcessWideNativeHeap", DisableParallelization = true)]
public sealed class ProcessWideNativeHeapCollection
{
}
