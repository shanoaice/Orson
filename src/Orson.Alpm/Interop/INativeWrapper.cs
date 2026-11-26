namespace Orson.Alpm.Interop;

/// <summary>Internal interface for managed wrappers exposing their raw underlying unmanaged struct pointer.</summary>
internal unsafe interface INativeWrapper
{
  void* BackingStruct { get; }
}
