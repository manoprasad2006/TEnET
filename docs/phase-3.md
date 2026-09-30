# Phase 3 — Tenant Resolution and Fail-Closed Middleware

## 1. Overview and Implementation Details

Phase 3 introduces strict multi-tenant context resolution, fail-closed tenant validation middleware, membership checks, structured logging scopes, and the temporary `/api/tenant/whoami` endpoint.

### Components Implemented

1. **Scoped `ITenantContext` and `TenantContext` (`Domain/TenantContext.cs`)**:
   - Holds `Guid? TenantId`, `string? TenantName`, and `bool IsSet`.
   - Thread-safe and scoped per request.
   - Enforces single-assignment: any attempt to call `SetTenant` when `IsSet == true` throws an `InvalidOperationException`.
   - Rejects empty GUIDs and null/empty tenant names with `ArgumentException`.
   - Separate `ITenantContextSetter` interface ensures consumers only get read-only access to tenant identity.

2. **Dedicated Unfiltered `TenantLookupContext` (`Data/TenantLookupContext.cs`)**:
   - Independent `DbContext` containing only `Tenants`, `AppUsers`, and `UserTenantMemberships`.
   - Completely free of tenant query filters or inventory/file data, providing an explicit, safe boundary for tenant discovery and membership checks before the request tenant context is established.

3. **Tenant Lookup Service (`Api/Tenancy/TenantLookupService.cs`)**:
   - Queries `TenantLookupContext` with `AsNoTracking()`.
   - Validates existence of tenant and checks whether the authenticated user has an active membership record in that tenant.

4. **Tenant Resolvers (`Api/Tenancy/`)**:
   - `ITenantResolver` returning `TenantResolutionResult` (`Success`, `Missing`, `Invalid`).
   - `HeaderTenantResolver`: Strict extraction of `X-Tenant-Id`.
     - Validates presence, single occurrence (rejects duplicate headers or comma-folded headers).
     - Enforces max length (64 characters) to reject oversized headers.
     - Parses exact 36-character hyphenated GUID format ("D"); rejects `Guid.Empty` and malformed/SQL-injection strings with 400 Bad Request before database access.
   - `SubdomainTenantResolver`: Disabled stub configurable via `Tenancy:Subdomain:Enabled` and `Tenancy:Subdomain:BaseDomain`.
   - `CompositeTenantResolver`: Ordered resolution across enabled resolvers. Crucially fails closed: if any resolver returns `Invalid` (malformed, duplicate, oversized), resolution aborts immediately with 400 without falling back or proceeding.

5. **Fail-Closed `TenantMiddleware` (`Api/Tenancy/TenantMiddleware.cs`)**:
   - Placed immediately after `UseAuthentication()` and before `UseRateLimiter()`, `UseAuthorization()`, and endpoint handlers.
   - Checks an explicit allowlist of exempt paths (`/hello`, `/health`, `/api/auth/login`, `/api/auth/me`) or endpoints with `IAllowAnonymous` metadata.
   - For all protected endpoints:
     - Missing header -> 400 Bad Request.
     - Malformed/duplicate/oversized header -> 400 Bad Request.
     - Unauthenticated user (missing JWT) -> 401 Unauthorized.
     - Unknown tenant ID (valid GUID not found in database) -> 404 Not Found.
     - Unauthorized tenant selection (authenticated user is not a member) -> 403 Forbidden.
     - Context set once per request.
     - Pushes a structured logging scope containing `TenantId`, `TenantName`, and `UserId` (no secrets logged).
     - Any unexpected exception fails closed with 500 without invoking `next(context)`.

6. **Temporary Endpoint `/api/tenant/whoami` (`Api/Tenancy/TenantController.cs`)**:
   - Protected endpoint returning the resolved tenant and authenticated user claims.

---

## 2. Exact PowerShell Commands

### Build and Run Tests
```powershell
Set-Location D:\Desktop\TenNet
Stop-Process -Name TenNet.Api -Force -ErrorAction SilentlyContinue
dotnet restore .\TenNet.sln
dotnet build .\TenNet.sln --no-restore
dotnet test .\TenNet.sln --no-build --no-restore
```

### Run the API Locally
```powershell
# Ensure JWT signing key and demo credentials are set in user-secrets
dotnet run --project .\Api --no-build --launch-profile http
```

### Verification with PowerShell / curl

```powershell
Set-Location D:\Desktop\TenNet

# 1. Anonymous exempt endpoints work without any headers
Invoke-RestMethod -Uri "http://127.0.0.1:5184/hello" -Method GET
Invoke-RestMethod -Uri "http://127.0.0.1:5184/health" -Method GET

# 2. Login as Alice (member of Acme and Globex)
$loginAlice = Invoke-RestMethod -Uri "http://127.0.0.1:5184/api/auth/login" -Method POST `
    -ContentType "application/json" -Body '{"email":"alice@tennet.example","password":"AcmeDemo!Alice2026"}'
$aliceToken = $loginAlice.accessToken
$acmeId = ($loginAlice.tenants | Where-Object { $_.name -eq "Acme" }).id
$globexId = ($loginAlice.tenants | Where-Object { $_.name -eq "Globex" }).id

# 3. Login as Bob (member of Initech only)
$loginBob = Invoke-RestMethod -Uri "http://127.0.0.1:5184/api/auth/login" -Method POST `
    -ContentType "application/json" -Body '{"email":"bob@tennet.example","password":"InitechDemo!Bob2026"}'
$bobToken = $loginBob.accessToken
$initechId = ($loginBob.tenants | Where-Object { $_.name -eq "Initech" }).id

# 4. /api/auth/me is exempt from tenant header and returns membership
Invoke-RestMethod -Uri "http://127.0.0.1:5184/api/auth/me" -Method GET `
    -Headers @{ Authorization = "Bearer $aliceToken" }

# 5. Valid tenant selection for Alice -> Acme (HTTP 200)
Invoke-RestMethod -Uri "http://127.0.0.1:5184/api/tenant/whoami" -Method GET `
    -Headers @{ Authorization = "Bearer $aliceToken"; "X-Tenant-Id" = $acmeId }

# 6. Valid tenant selection for Alice -> Globex (HTTP 200)
Invoke-RestMethod -Uri "http://127.0.0.1:5184/api/tenant/whoami" -Method GET `
    -Headers @{ Authorization = "Bearer $aliceToken"; "X-Tenant-Id" = $globexId }

# 7. Missing X-Tenant-Id header returns 400 Bad Request
try {
    Invoke-RestMethod -Uri "http://127.0.0.1:5184/api/tenant/whoami" -Method GET `
        -Headers @{ Authorization = "Bearer $aliceToken" }
} catch {
    $_.Exception.Response.StatusCode # 400
}

# 8. Malformed X-Tenant-Id header returns 400 Bad Request
try {
    Invoke-RestMethod -Uri "http://127.0.0.1:5184/api/tenant/whoami" -Method GET `
        -Headers @{ Authorization = "Bearer $aliceToken"; "X-Tenant-Id" = "not-a-guid" }
} catch {
    $_.Exception.Response.StatusCode # 400
}

# 9. SQL-injection-style X-Tenant-Id header returns 400 Bad Request
try {
    Invoke-RestMethod -Uri "http://127.0.0.1:5184/api/tenant/whoami" -Method GET `
        -Headers @{ Authorization = "Bearer $aliceToken"; "X-Tenant-Id" = "' OR '1'='1" }
} catch {
    $_.Exception.Response.StatusCode # 400
}

# 10. Unknown tenant ID (valid GUID not in DB) returns 404 Not Found
try {
    Invoke-RestMethod -Uri "http://127.0.0.1:5184/api/tenant/whoami" -Method GET `
        -Headers @{ Authorization = "Bearer $aliceToken"; "X-Tenant-Id" = "a0000000-0000-0000-0000-000000000000" }
} catch {
    $_.Exception.Response.StatusCode # 404
}

# 11. Unauthorized tenant selection (Bob attempting to select Acme) returns 403 Forbidden
try {
    Invoke-RestMethod -Uri "http://127.0.0.1:5184/api/tenant/whoami" -Method GET `
        -Headers @{ Authorization = "Bearer $bobToken"; "X-Tenant-Id" = $acmeId }
} catch {
    $_.Exception.Response.StatusCode # 403
}
```

---

## 3. Test Results

Execution of `dotnet test .\TenNet.sln`:
```text
Passed!  - Failed:     0, Passed:    47, Skipped:     0, Total:    47, Duration: 7 s - TenNet.Tests.dll (net8.0)
```

### Test Coverage Summary
- `TenantContextRejectsSettingTwice`: Confirms `SetTenant` throws `InvalidOperationException` on subsequent invocations.
- `TenantContextRejectsEmptyGuidOrNullName`: Confirms `ArgumentException` on invalid arguments.
- `ValidTenantSelectionReturnsWhoAmI`: Confirms Alice successfully accesses Acme and Globex with proper context resolution.
- `MissingTenantHeaderReturns400`: Confirms 400 Bad Request when header is omitted.
- `MalformedTenantHeaderReturns400`: Confirms 400 Bad Request across non-guid strings, empty GUIDs, unhyphenated hex, and braces.
- `DuplicateTenantHeaderReturns400`: Confirms 400 Bad Request for both multi-header arrays and comma-separated folded headers.
- `OversizedTenantHeaderReturns400`: Confirms 400 Bad Request for oversized headers (> 64 characters).
- `SqlInjectionStyleTenantHeaderReturns400`: Confirms SQL injection strings are safely rejected before reaching the database.
- `UnknownTenantIdReturns404`: Confirms 404 Not Found for non-existent GUIDs.
- `UnauthorizedTenantSelectionReturns403`: Confirms 403 Forbidden when a user requests a tenant they do not belong to.
- `UnauthenticatedTenantRequestReturns401`: Confirms 401 Unauthorized when no JWT is provided.
- `AnonymousAndExemptEndpointsBypassTenantResolution`: Confirms `/hello`, `/health`, `/api/auth/login`, and `/api/auth/me` operate without requiring tenant headers.
- `SubdomainTenantResolverWhenDisabledReturnsMissing`: Confirms disabled stub status.
- `CompositeResolverEvaluatesInOrderAndFailsClosedOnInvalid`: Confirms composite ordering and fail-closed behavior on invalid outcomes.

---

## 4. Security Review & Residual Risks

### "How could a malicious tenant bypass this?"
1. **Direct Database / Memory Access**: An attacker with direct file access to SQLite or container memory can bypass middleware checks. SQLite files must remain secured at the OS level.
2. **Exempt Endpoint Abuse**: Endpoints on the exempt allowlist (`/hello`, `/health`, `/api/auth/login`, `/api/auth/me`) do not have tenant context set. If any developer erroneously queries tenant-scoped entities inside an exempt endpoint prior to Phase 4 query filters, cross-tenant data leaks could occur. (Phase 4 addresses this by ensuring query filters match zero rows when no tenant context is set).
3. **Subdomain Hijacking (when enabled in future)**: If `SubdomainTenantResolver` is enabled in production without strict host validation or behind a misconfigured reverse proxy, an attacker could spoof the `Host` header. It remains disabled by default.
4. **Header Injection via Untrusted Proxies**: If upstream reverse proxies fail to strip untrusted `X-Tenant-Id` headers from external clients or improperly concatenate duplicate headers, clients could attempt spoofing. Our resolver strictly enforces single-value checks and rejects comma-separated values.
5. **Membership Revocation Window**: Memberships are validated against the database on each request via `TenantLookupService`. Therefore, revoking a user's membership in `UserTenantMemberships` immediately stops them from accessing that tenant on their next request, even if their JWT is still valid.

---

## 5. Five-Question Quiz: Middleware Order and Scoped DI

Here is your five-question quiz regarding middleware ordering and scoped dependency injection in ASP.NET Core:

1. **Why must `TenantMiddleware` be placed AFTER `UseAuthentication()` but BEFORE `UseAuthorization()`?**
   - *Question detail*: What specific data would be missing if it ran before `UseAuthentication()`, and what security gap would occur if it ran after `UseAuthorization()`?

2. **Why is `TenantMiddleware` implemented with singleton constructor dependencies (`RequestDelegate`, `ILogger`) while `ITenantContextSetter`, `CompositeTenantResolver`, and `ITenantLookupService` are injected directly into `InvokeAsync`?**
   - *Question detail*: What would happen if a scoped service like `TenantContext` were injected into the middleware's constructor instead of `InvokeAsync`?

3. **If a request path matches an exempt endpoint (such as `/api/auth/me`), `TenantMiddleware` calls `await next(context); return;` without setting `TenantContext`. What is the state of `ITenantContext` during that request, and why is this design safe?**

4. **Why must `HeaderTenantResolver` reject duplicate headers (both multiple header entries and comma-separated values) with `400 Bad Request` rather than simply picking the first value?**

5. **In `TenantMiddleware`, why is `TenantLookupContext` used for membership verification rather than `AppDbContext`?**
