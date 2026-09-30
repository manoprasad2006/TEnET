# TenNet — Multi-Tenant Inventory Platform Handover & Runbook

This document details the complete end-to-end architecture, implementation, threat model, verification commands, and demo runbook for all completed phases (Phase 0 through Phase 10).

---

## 1. Quick Start: Setting AWS S3 Credentials in User-Secrets

If you have your AWS S3 credentials ready, execute the following PowerShell commands to configure the API:

```powershell
Set-Location D:\Desktop\TenNet

# AWS S3 Production/Staging Credentials
dotnet user-secrets set 'Storage:ServiceUrl' '' --project .\Api # Leave empty for standard AWS S3
dotnet user-secrets set 'Storage:Region' 'us-east-1' --project .\Api # Replace with your AWS Region
dotnet user-secrets set 'Storage:AccessKey' 'YOUR_AWS_ACCESS_KEY' --project .\Api
dotnet user-secrets set 'Storage:SecretKey' 'YOUR_AWS_SECRET_KEY' --project .\Api
dotnet user-secrets set 'Storage:BucketName' 'YOUR_BUCKET_NAME' --project .\Api
dotnet user-secrets set 'Storage:ForcePathStyle' 'false' --project .\Api
```

For local MinIO execution without AWS:
```powershell
# Using Docker Compose MinIO
docker compose up -d
dotnet user-secrets set 'Storage:ServiceUrl' 'http://127.0.0.1:9000' --project .\Api
dotnet user-secrets set 'Storage:Region' 'us-east-1' --project .\Api
dotnet user-secrets set 'Storage:AccessKey' 'tennetadmin' --project .\Api
dotnet user-secrets set 'Storage:SecretKey' 'TenNetMinioDemo2026!' --project .\Api
dotnet user-secrets set 'Storage:BucketName' 'tennet-inventory' --project .\Api
dotnet user-secrets set 'Storage:ForcePathStyle' 'true' --project .\Api
```

---

## 2. Requirement Matrix & Verification Guide

| Requirement | Implementation Location | Proving Test / Verification Command |
| --- | --- | --- |
| **Fail-Closed Tenant Middleware** | `Api/Tenancy/TenantMiddleware.cs` | `TenantResolutionTests.cs` (`MissingTenantHeaderReturns400`, `UnknownTenantIdReturns404`, `UnauthorizedTenantSelectionReturns403`) |
| **Strict Header Validation** | `Api/Tenancy/HeaderTenantResolver.cs` | `TenantResolutionTests.cs` (`MalformedTenantHeaderReturns400`, `DuplicateTenantHeaderReturns400`, `OversizedTenantHeaderReturns400`, `SqlInjectionStyleTenantHeaderReturns400`) |
| **Reflection Global Query Filters** | `Data/AppDbContext.cs` | `EfIsolationTests.cs` (`TenantACannotSeeTenantBItems`, `EmptyTenantContextReturnsZeroRowsAndRejectsWrites`) |
| **TenantId Stamping & Immutability** | `Data/AppDbContext.cs` | `EfIsolationTests.cs` (`SaveChangesStampsTenantIdIgnoringClientValue`, `ChangingTenantIdThrowsTenantViolationException`) |
| **Forbidden EF Bypass APIs Guard** | `Tests/ArchitectureTests.cs` | `ArchitectureTests.cs` (`ProductionCodeMustNotUseForbiddenEfBypassApis`) scans all `.cs` files |
| **Thin Inventory CRUD Controllers** | `Api/Inventory/ItemsController.cs`, `ItemService.cs` | `InventoryCrudTests.cs` (`ListItemsReturnsPaginatedBoundedResultsForCurrentTenantOnly`) |
| **Duplicate SKU Scoped to Tenant** | `Api/Inventory/ItemService.cs` | `InventoryCrudTests.cs` (`DuplicateSkuInSameTenantReturns409Conflict`, `SameSkuInDifferentTenantIsAllowed`) |
| **RowVersion Concurrency / ETag (409)** | `Api/Inventory/ItemService.cs`, `Data/AppDbContext.cs` | `InventoryCrudTests.cs` (`UpdateItemRequiresRowVersionAndRejectsConcurrencyConflicts`) |
| **Audit Logs on Item & File Mutations** | `ItemService.cs`, `FilesController.cs`, `AppDbContext.cs` | `InventoryCrudTests.cs`, `FileStorageTests.cs` |
| **S3 / MinIO Safe File Storage** | `Api/Storage/S3FileStorage.cs`, `FilesController.cs` | `FileStorageTests.cs` (`UploadValidPngAndDownloadSucceedsWithAudit`) |
| **Magic Byte & Extension Validation** | `Api/Storage/FilesController.cs` | `FileStorageTests.cs` (`UploadRejectsBadMagicBytesDoubleExtensionsAndDisallowedExtensions`) |
| **Cross-Tenant File Access Isolation** | `Api/Storage/FilesController.cs` | `FileStorageTests.cs` (`CrossTenantFileAccessIsBlocked`) |
| **React + TypeScript Frontend** | `Frontend/src/` | `npm run build` in `Frontend/` (Vite bundle built in 1.68s, 0 errors) |
| **Security Headers & CORS Hardening** | `Api/Program.cs` | Inspect HTTP response headers via PowerShell / curl |

---

## 3. Threat Model & Mitigations

| Threat | Vulnerability Mechanism | TenNet Mitigation | Proving Automated Test |
| --- | --- | --- | --- |
| **Header Spoofing** | Attacker injects another tenant's ID into `X-Tenant-Id` | `TenantMiddleware` checks real-time database membership via `TenantLookupService`; non-members receive generic `403 Forbidden` | `TenantResolutionTests.UnauthorizedTenantSelectionReturns403` |
| **IDOR / Cross-Tenant Reads** | Attacker accesses an item ID belonging to another tenant (`/api/items/{id}`) | EF Core global query filter strips other tenants; returns identical `404 Not Found` preventing enumeration | `InventoryCrudTests.CrossTenantAndNonExistentItemIdsReturnIdentical404` |
| **Mass Assignment / Tenant Tampering** | Attacker submits a JSON payload with a different `tenantId` | DTOs do not accept `TenantId`; `AppDbContext.PrepareChanges` forcibly overwrites `TenantId` with context value | `EfIsolationTests.SaveChangesStampsTenantIdIgnoringClientValue` |
| **Tenant Migration Attack** | Attacker attempts to change `TenantId` on an existing item | `AppDbContext.PrepareChanges` detects modified `TenantId` and throws `TenantViolationException` | `EfIsolationTests.ChangingTenantIdThrowsTenantViolationException` |
| **Unset Tenant Data Leak** | Code path executes without setting tenant context | Reflection query filter evaluates to `false` when `!IsSet`, returning 0 rows; writes fail closed | `EfIsolationTests.EmptyTenantContextReturnsZeroRowsAndRejectsWrites` |
| **Query Filter Bypass** | Developer calls `IgnoreQueryFilters()`, `FromSqlRaw()`, or `ExecuteUpdate()` | Automated architecture test scans all source code and fails CI/CD if any bypass API is introduced | `ArchitectureTests.ProductionCodeMustNotUseForbiddenEfBypassApis` |
| **Cross-Tenant File Access** | Attacker guesses file ID or S3 object key | Keys are server-generated (`tenants/{id}/{itemId}/{guid}`); controller checks tenant key prefix before S3 read | `FileStorageTests.CrossTenantFileAccessIsBlocked` |
| **Malicious Upload / Path Traversal** | Attacker uploads `shell.php.png` or `../../secret` | Filenames sanitized, double extensions rejected, magic bytes checked, client name never used in S3 key | `FileStorageTests.UploadRejectsBadMagicBytesDoubleExtensionsAndDisallowedExtensions` |
| **XSS via File Download** | Attacker uploads SVG/HTML with JavaScript | Non-images forced to `Content-Disposition: attachment`; CSP disallows execution | `FilesController.DownloadFile` |
| **Brute Force Login** | Attacker guesses user passwords | Fixed-window IP rate limiting (5 req/min) returning 429 with `Retry-After` header | `AuthTests.LoginIsLimitedToFiveAttemptsAndForwardedHeaderCannotBypassIt` |
| **Token Tampering** | Attacker alters claims or signs with weak key | ASP.NET Core JwtBearer validation verifies HMAC-SHA256 signature, expiry, issuer, audience | `SecurityIsolationTests.TamperedExpiredAndWrongAudienceJwtsReturn401` |
| **Concurrent Interleaved Leaks** | Simultaneous requests from different tenants cross-contaminate | `ITenantContext` and `AppDbContext` are scoped per HTTP request; logging scopes are isolated | `SecurityIsolationTests.ConcurrentInterleavedRequestsForDifferentTenantsNeverLeakData` |

---

## 4. Four-to-Five-Minute Demonstration Script

1. **Step 1: Start Services**
   - Run API: `dotnet run --project .\Api --no-build --launch-profile http`
   - Run Frontend: `npm run dev` in `.\Frontend`
   - Open browser at `http://127.0.0.1:5173`.
2. **Step 2: Sign in as Alice (Multi-Tenant User)**
   - Click demo button **Alice**. Shows email `alice@tennet.example`.
   - Click **Sign In**.
   - Note the tenant selector dropdown is populated with **Acme** and **Globex** (queried from `/api/auth/me`).
   - Active tenant badge displays `Active: Acme`.
   - Table displays Acme items (e.g. `ACM-DRILL-001`, `ACM-BOLT-002`).
3. **Step 3: Tenant Switching & Isolation**
   - Switch tenant selector to **Globex**.
   - Note that inventory table immediately clears and loads Globex items (e.g. `GLX-SENSOR-001`, `GLX-RELAY-002`). Zero Acme items appear.
   - Switch back to **Acme**.
4. **Step 4: Inventory CRUD & Concurrency**
   - Click **Add Item**. Enter `Name: Laser Level`, `SKU: ACM-LASER-001`, `Quantity: 15`, `Price: 149.99`.
   - Save. Item appears in Acme's inventory.
   - In a second tab or PowerShell, edit the item, then try updating in the UI to demonstrate instant `409 Conflict` (RowVersion mismatch protection).
5. **Step 5: File Attachments**
   - Expand an item row.
   - Click **Upload File**. Choose a valid PNG or PDF.
   - The file uploads to S3/MinIO under key `tenants/{tenantId}/{itemId}/{fileId}`.
   - Click download icon to download the attachment.
6. **Step 6: Sign Out and Sign in as Bob (Single-Tenant User)**
   - Click **Sign Out**.
   - Click demo button **Bob** (`bob@tennet.example`). Sign In.
   - Tenant selector only contains **Initech**.
   - Initech items (e.g. `INT-KEYBOARD-001`) display. Bob cannot see Acme or Globex items.
   - In PowerShell, spoofing `X-Tenant-Id: <AcmeId>` using Bob's JWT returns `403 Forbidden`.
