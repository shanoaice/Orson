# A public member is a property unless it takes an argument, needs a verb, or does work

---
status: accepted
---

The FDG says a type exposes state as properties and behavior as methods. It does not say which member
is which, and across the FFI boundary the usual test — a getter with no parameter is a property — is
wrong often enough to have broken a build. We decide with three questions:

1. **Does it take an argument, or is it a conversion?** A property cannot take a parameter; the
   indexer is the language exception, below. A conversion takes one and says so in its name: `ToX`,
   `FromX`.
2. **Does its honest name need a verb?** `Load`, `Check`, `Validate`, `Compute`. A member whose
   summary only stays honest by smuggling a verb into it is a method wearing a noun.
3. **Does libalpm do work on the call?** Loading, parsing, validating, or allocating memory the
   caller later frees. The question also answers yes when the cost is far above a field read.

All three answer no → property. Any answers yes → method.

## Why "can it fail?" is not a fourth question

Every member here crosses into libalpm, where a failure return is as ordinary on a field read as on a
parse. As a criterion, failure selects almost everything and separates nothing. It is already
answered elsewhere: [ADR 0001](./0001-fsharp-interop-boundary-and-try-seam.md) carries failure through
the `Try*` seam, and [ADR 0007](./0007-native-errno-and-exception-contract.md) maps it into one public
exception type. Shape and failure are orthogonal, so a property that throws is still a
property. `Database.Servers` throws `AlpmLifetimeException` once the database is released, and it
stays a property.

## Work decides the close calls

`Database.Servers` and `Database.LoadPackageCache()` are the pair to keep in mind. Both answer a
question about the database, and neither takes a parameter.

`alpm_db_get_servers` returns a list libalpm already holds. The call copies nothing, allocates
nothing, and costs what a field read costs. Question 3 answers no. → property.

`alpm_db_get_pkgcache` parses the database file the first time it is called. Later calls answer from
the populated cache and are cheap, but the shape has to price the first call, not the warm one. A
property whose first access parses the whole database is a silent lie about what
`db.PackageCache.Count` does. Question 3 answers yes. → method.

Question 3 outranks question 2. When the two disagree, the work is the fact and the name is the
choice. A noun-named member whose call parses a file is a method; the rename to a verb name is a
separate cleanup, not a condition for the method shape.

## The indexer is the one property-shaped member that takes an argument

`FileList.this[int]` answers question 1 with yes and would be a method by the ladder. C# gives an
element container a property form that takes the index, so the method form would buy nothing and cost
call-site noise. The exception is narrow: a member takes the indexer form only when the index selects
one of its own elements. `Database.GetGroup(string)` stays a method, because the string selects a
group out of libalpm's registry rather than an element out of the database.

## Motivation and Background

A mechanical "turn all getters into properties" pass previously converted `GetPackageCache`,
`GetGroupCache`, `GetServers`, and `GetCacheServers` into properties. Two were right and two were wrong.
`PackageCache` and `GroupCache` parse database files on first access, which broke downstream benchmarks;
`Servers` and `CacheServers` only hand back pointers libalpm already holds.
The subsequent fix restored `LoadPackageCache()` and `LoadGroupCache()` as methods, keeping `Servers`
and `CacheServers` as properties.
This demonstrates why the criterion must be arguments, verbs, and work rather than a simplistic
"is it a zero-parameter getter" rule.
## Considered Options

**"A getter with no parameter is a property."** This is the rule the pass applied. It holds for a
CLR object, where a getter is a field read plus a call. It does not hold here, where the first call
may parse the database. A signature does not show cost, so cost has to be asked.

**"Every member that crosses FFI is a method, because every call can fail."** This throws away the
shape that makes the surface readable and prices a field read at the same rate as a parse. Failure is
carried by the `Try*` seam (ADR 0001) and is independent of shape.
**"A property that computes lazily on first access."** This is the `PackageCache` variant that looks
harmless: the property caches its result. It still makes the first access parse the database, and the
caller cannot see that from the signature. Lazy initialization is an implementation choice; it is not
a reason to hide a cost behind a field read.

**"Mark the expensive properties with a suffix or an attribute."** A convention such as `*Slow`
encodes a cost the caller cannot verify, and the BCL has no precedent for it. The verb in the name
already says the call does work, which is why question 2 exists.

## Consequences

- A new public member is a property until a question says otherwise. Review argues from the three
  questions, not from "it is a getter".
- The cost of the call has to be known before the shape is chosen. Read the libalpm header: a
  `_get_` that walks a list or parses a file counts as work under question 3.
- Each member's remarks keep the local reason in its own words; the rule lives here.
  `Database.LoadPackageCache` and `Database.LoadGroupCache` state why they are methods, and
  `LoadPackageCache` cites this ADR.
- A property that throws keeps its shape. The `Try*` twins are internal (ADR 0001), so adding one
  does not change the public shape.
- This is a repository convention, like the enum placement in ADR 0014. The FDG governs the surface
  but assumes a getter is cheap, and that assumption does not survive the FFI boundary. That is why
  question 3 is written down.

## See also
- [ADR 0001: F# interop boundary and Try seam](./0001-fsharp-interop-boundary-and-try-seam.md)
- [ADR 0007: Native errno capture and exception mapping](./0007-native-errno-and-exception-contract.md)
- [ADR 0014: Enum file placement is determined by primary consumer](./0014-enum-file-placement-by-consumer.md)
