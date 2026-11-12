# .NET and EF Core support update

Support .NET 10 and 11; remove .NET 8 and 9. Allow EF Core and PostgreSQL/SQL Server provider versions `[10.0.0,12.0.0)`. Replace Pomelo with stable Microting for MySQL, retaining its upstream EF Core constraints. Preserve public locking APIs and existing behavior.

## Approach

Update the existing multi-target packages, central test dependencies, CI, release tooling, samples, benchmarks, and support documentation together. Compile against EF Core 10 where possible; exercise SQL Server against EF Core 10 and 11. PostgreSQL and MySQL use stable EF Core 10 providers on both runtimes until their stable EF Core 11 providers are available. Do not introduce preview dependencies or suppress incompatible dependency warnings.

Keeping .NET 8/9 contradicts the requested support policy; adopting preview providers would change the agreed stable dependency policy. Existing real provider SQL-generation tests and integration suites verify compatibility; inspect packed dependency ranges as well as builds.

The user approved these requirements and proceeding with the PR in this conversation.
