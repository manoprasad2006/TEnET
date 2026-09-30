# Tests

Phase 1 adds real SQLite tests for migrations, seed idempotence, password hashing,
concurrency conflicts, composite file foreign keys, SKU uniqueness, restricted
deletion and the Development-only seed guard. Run `dotnet test TenNet.sln`.
Phase 2 adds WebApplicationFactory tests with independent SQLite in-memory
databases, real JWT validation, membership refresh, login DTO validation,
generic failure timing, fallback authorization and login rate limiting.
Run `dotnet test TenNet.sln --filter FullyQualifiedName~AuthTests` for auth only.
Phase 6 expands this into the complete tenant isolation test suite.
Phase 7 adds file validation tests and a fake IFileStorage implementation.
