using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// Options for the ALPM library.
/// Options of array type are queried and modified through properties exposed as <see cref="ICollection{T}"/>s.
/// Options of scalar type are queried and modified through properties whose accessors call the
/// underlying ALPM functions.
/// </summary>
public sealed unsafe class AlpmOptions
{
  private readonly SafeAlpmHandle _handle;

  // The session's lifetime domain. The option collections are cached views over libalpm's handle state:
  // they carry the lifetime domain so lists handed out by their enumerators inherit that domain,
  // while the option wrapper and its collections remain valid until the session handle is disposed.
  private readonly RootLifetime _lifetime;

  internal AlpmOptions(SafeAlpmHandle handle, RootLifetime lifetime)
  {
    _handle = handle;
    _lifetime = lifetime;

    Architectures = new Architecture(handle, lifetime);
    AssumeInstalled = new AssumeInstalled(handle, lifetime);
    CacheDirectories = new CacheDirectories(handle, lifetime);
    OverwritableFiles = new OverwritableFiles(handle, lifetime);
    HookDirectories = new HookDirectories(handle, lifetime);
    IgnoreGroups = new IgnoreGroups(handle, lifetime);
    IgnorePackages = new IgnorePackages(handle, lifetime);
    NoExtract = new NoExtract(handle, lifetime);
    NoUpgrade = new NoUpgrade(handle, lifetime);
  }

  /// <summary>
  /// Throws for a non-zero libalpm return value, reading the handle's errno right here.
  /// </summary>
  /// <param name="err">The value the option setter returned; zero means success.</param>
  /// <param name="operation">
  /// The member that issued the call, so the failure names the option it was setting. Callers pass
  /// nothing; the compiler fills it in.
  /// </param>
  private void ThrowIfError(int err, [CallerMemberName] string? operation = null)
  {
    if (err == 0) return;
    GC.KeepAlive(this);
    throw NativeCall.Failure(_handle, $"set {operation ?? "option"}").ToException();
  }

  private T Read<T>(delegate* managed<SafeAlpmHandle, T> reader)
  {
    _handle.ThrowIfDisposed();
    var val = reader(_handle);
    GC.KeepAlive(this);
    return val;
  }

  private string? ReadString(delegate* managed<SafeAlpmHandle, byte*> reader)
  {
    _handle.ThrowIfDisposed();
    var val = NativeString.FromNative((nint)reader(_handle));
    GC.KeepAlive(this);
    return val;
  }

  private void Write(delegate* managed<SafeAlpmHandle, int, int> writer, int value, [CallerMemberName] string? operation = null)
  {
    _handle.ThrowIfDisposed();
    ThrowIfError(writer(_handle, value), operation);
    GC.KeepAlive(this);
  }

  private void WriteUInt(delegate* managed<SafeAlpmHandle, uint, int> writer, uint value, [CallerMemberName] string? operation = null)
  {
    _handle.ThrowIfDisposed();
    ThrowIfError(writer(_handle, value), operation);
    GC.KeepAlive(this);
  }

  private void WriteUShort(delegate* managed<SafeAlpmHandle, ushort, int> writer, ushort value, [CallerMemberName] string? operation = null)
  {
    _handle.ThrowIfDisposed();
    ThrowIfError(writer(_handle, value), operation);
    GC.KeepAlive(this);
  }
  /// <summary>
  /// Marshals <paramref name="value"/> for a string option setter and throws on failure.
  /// </summary>
  /// <remarks>
  /// Option values can be paths or URLs (logfile, gpgdir, dbext), hence the 256-byte scratch; the
  /// buffer is released by the same frame that allocated it.
  /// <para>
  /// The setter arrives as a <c>delegate* managed</c> because <c>delegate* unmanaged[Cdecl]</c>
  /// cannot point at a <c>[DllImport]</c> method (CS8786).
  /// </para>
  /// </remarks>
  private void SetStringOption(string value, delegate* managed<SafeAlpmHandle, byte*, int> setter,
    [CallerMemberName] string? operation = null)
  {
    ArgumentNullException.ThrowIfNull(value);
    _handle.ThrowIfDisposed();
    Span<byte> scratch = stackalloc byte[256];
    using var buffer = new Utf8Buffer(value, scratch);
    ThrowIfError(setter(_handle, buffer.Ptr), operation);
  }

  public ICollection<string> Architectures { get; }

  public ICollection<Depend> AssumeInstalled { get; }

  public ICollection<string> CacheDirectories { get; }

  public ICollection<string> OverwritableFiles { get; }

  public ICollection<string> HookDirectories { get; }

  public ICollection<string> IgnoreGroups { get; }

  public ICollection<string> IgnorePackages { get; }

  public ICollection<string> NoExtract { get; }

  public ICollection<string> NoUpgrade { get; }
  public bool CheckSpace
  {
    get => Read(&NativeMethods.alpm_option_get_checkspace) != 0;
    set => Write(&NativeMethods.alpm_option_set_checkspace, value ? 1 : 0);
  }

  public string DatabaseExtension
  {
    get => ReadString(&NativeMethods.alpm_option_get_dbext)!;
    set => SetStringOption(value, &NativeMethods.alpm_option_set_dbext);
  }

  public string DatabasePath
  {
    get => ReadString(&NativeMethods.alpm_option_get_dbpath)!;
  }

  public string Root
  {
    get => ReadString(&NativeMethods.alpm_option_get_root)!;
  }

  public SigLevel DefaultSigLevel
  {
    get => (SigLevel)Read(&NativeMethods.alpm_option_get_default_siglevel);
    set => Write(&NativeMethods.alpm_option_set_default_siglevel, (int)value);
  }

  public SigLevel LocalFileSigLevel
  {
    get => (SigLevel)Read(&NativeMethods.alpm_option_get_local_file_siglevel);
    set => Write(&NativeMethods.alpm_option_set_local_file_siglevel, (int)value);
  }

  public SigLevel RemoteFileSigLevel
  {
    get => (SigLevel)Read(&NativeMethods.alpm_option_get_remote_file_siglevel);
    set => Write(&NativeMethods.alpm_option_set_remote_file_siglevel, (int)value);
  }

  public int ParallelDownloads
  {
    get => Read(&NativeMethods.alpm_option_get_parallel_downloads);
    set => WriteUInt(&NativeMethods.alpm_option_set_parallel_downloads, (uint)value);
  }

  /// <summary>
  /// The logfile path, or <c>null</c> when libalpm has none configured (its default).
  /// </summary>
  /// <remarks>
  /// Once configured, libalpm does not support unsetting the logfile back to <c>null</c>.
  /// Attempting to assign <c>null</c> throws an <see cref="ArgumentNullException"/>.
  /// </remarks>
  [DisallowNull]
  public string? LogFile
  {
    get => ReadString(&NativeMethods.alpm_option_get_logfile);
    set => SetStringOption(value, &NativeMethods.alpm_option_set_logfile);
  }

  public bool UseSyslog
  {
    get => Read(&NativeMethods.alpm_option_get_usesyslog) != 0;
    set => Write(&NativeMethods.alpm_option_set_usesyslog, value ? 1 : 0);
  }

  public string Lockfile
  {
    get => ReadString(&NativeMethods.alpm_option_get_lockfile)!;
  }

  /// <summary>
  /// libalpm's GnuPG home directory, or <c>null</c> when it has none configured (its default).
  /// </summary>
  /// <remarks>
  /// Once configured, libalpm does not support unsetting the GnuPG home directory back to <c>null</c>.
  /// Attempting to assign <c>null</c> throws an <see cref="ArgumentNullException"/>.
  /// </remarks>
  [DisallowNull]
  public string? GpgDirectory
  {
    get => ReadString(&NativeMethods.alpm_option_get_gpgdir);
    set => SetStringOption(value, &NativeMethods.alpm_option_set_gpgdir);
  }

  /// <summary>
  /// Enables or disables the download timeout.
  /// </summary>
  public bool DisableDownloadTimeout
  {
    get => Read(&NativeMethods.alpm_option_get_disable_dl_timeout) != 0;
    set => WriteUShort(&NativeMethods.alpm_option_set_disable_dl_timeout, (ushort)(value ? 1 : 0));
  }

  /// <summary>
  /// Enables or disables the filesystem portion of the sandbox.
  /// </summary>
  public bool DisableSandboxFilesystem
  {
    get => Read(&NativeMethods.alpm_option_get_disable_sandbox_filesystem) != 0;
    set => WriteUShort(&NativeMethods.alpm_option_set_disable_sandbox_filesystem, (ushort)(value ? 1 : 0));
  }

  /// <summary>
  /// Enables or disables the network portion of the sandbox.
  /// </summary>
  public bool DisableSandboxNetwork
  {
    get => Read(&NativeMethods.alpm_option_get_disable_sandbox_network) != 0;
    set => WriteUShort(&NativeMethods.alpm_option_set_disable_sandbox_network, (ushort)(value ? 1 : 0));
  }

  /// <summary>
  /// Enables or disables the syscalls portion of the sandbox.
  /// </summary>
  public bool DisableSandboxSyscalls
  {
    get => Read(&NativeMethods.alpm_option_get_disable_sandbox_syscalls) != 0;
    set => WriteUShort(&NativeMethods.alpm_option_set_disable_sandbox_syscalls, (ushort)(value ? 1 : 0));
  }

  /// <summary>
  /// Gets or sets the user to switch to for sandboxed operations, or <c>null</c> when none is set.
  /// </summary>
  public string? SandboxUser
  {
    get => ReadString(&NativeMethods.alpm_option_get_sandboxuser);
    set
    {
      _handle.ThrowIfDisposed();
      if (value is null)
      {
        ThrowIfError(NativeMethods.alpm_option_set_sandboxuser(_handle, null));
        GC.KeepAlive(this);
        return;
      }

      Span<byte> scratch = stackalloc byte[64];
      using var buffer = new Utf8Buffer(value, scratch);
      var err = NativeMethods.alpm_option_set_sandboxuser(_handle, buffer.Ptr);
      if (err == 1)
      {
        throw new ArgumentException($"The user '{value}' is not known on this system.", nameof(value));
      }
      ThrowIfError(err);
      GC.KeepAlive(this);
    }
  }
}
