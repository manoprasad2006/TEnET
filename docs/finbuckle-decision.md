# Finbuckle.MultiTenant reference

Reviewed for TenNet on 30 September 2026.

## Decision

Use Finbuckle as an architectural reference for Phases 3 and 4. Consider its
ASP.NET Core tenant-resolution component behind TenNet's ITenantResolver when
implementing Phase 3. Verify the selected package targets .NET 8 before adding it;
the repository's current main release targets .NET 10.

Keep the required Guid TenantId model and automatic ITenantEntity query filters.
The reviewed v8 EF integration uses a string TenantId and explicit entity opt-in,
so it is not a direct replacement for TenNet's required persistence design.

## Requirements that remain TenNet's responsibility

- Authenticate first; validate one bounded GUID header; verify user membership.
- Reject missing, malformed, unknown or unauthorized tenants before data routes.
- Set the scoped tenant context once, after authorization; provide no default tenant.
- Filter every ITenantEntity automatically and enforce tenant ownership on saves.
- Keep JWT validation, private S3 keys, file validation and the isolation tests.

No package or application code was changed by this review. Phase 2 remains next;
implementation waits for the user's "next" under the agreed phase workflow.

## Sources

- [Repository](https://github.com/finbuckle/finbuckle.multitenant)
- [v8 tenant strategies](https://www.finbuckle.com/MultiTenant/Docs/v8.1.18/Strategies)
- [v8 EF Core integration](https://www.finbuckle.com/MultiTenant/Docs/v8.1.18/EFCore)
