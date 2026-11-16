using Orson.Alpm.Bindings;

namespace Orson.Alpm;

/// <summary>The local package database representing installed packages.</summary>
/// <remarks>
/// A borrowed handle: libalpm owns the database, this wrapper only reads through it. The local database
/// cannot be unregistered. It is invalidated and re-opened when a transaction commits or when the owning
/// <see cref="AlpmSession" /> is disposed.
/// </remarks>
public sealed unsafe class LocalDatabase : Database
{
  internal LocalDatabase(_alpm_db_t* backingStruct, ChildLifetime lifetime)
    : base(backingStruct, lifetime)
  {
  }
}
