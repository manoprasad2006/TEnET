# Phase 2 — Authentication with JWT and memberships

## 1. Exact commands

The code is implemented; no schema migration is needed. Stop any running API
before building on Windows because its executable is locked while running.

```powershell
Set-Location D:\Desktop\TenNet
dotnet restore .\TenNet.sln
dotnet build .\TenNet.sln --no-restore
dotnet test .\TenNet.sln --no-build --no-restore
```

New dependencies already pinned in project files:

```powershell
dotnet add .\Api\TenNet.Api.csproj package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.31
dotnet add .\Tests\TenNet.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing --version 8.0.31
```

Generate a signing key once, if one is not already configured. This was done on
the current machine. Demo user passwords and their setup commands remain only
in the root README.

```powershell
$keyBytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($keyBytes)
$rng.Dispose()
$jwtKey = [Convert]::ToBase64String($keyBytes)
dotnet user-secrets set 'Jwt:SigningKey' $jwtKey --project .\Api | Out-Null
Remove-Variable jwtKey, keyBytes
dotnet run --project .\Api --no-build --launch-profile http
```

Configuration equivalent: `Jwt__SigningKey` in the process environment. The value
must be Base64 of at least 32 random bytes. The application rejects a missing or
weak key in all environments. Never print the key, token or password.

## 2. Every file with full code

The full source is in [phase-2-code.md](phase-2-code.md). The updated root README
is linked separately to keep demo credentials documented only there. No Domain,
Data or migration files were changed in this phase.

## 3. Key pieces

- **JwtSettings:** validates configuration at startup. The same issuer, audience,
  key, HS256 algorithm and time rules govern issuance and bearer validation.
- **LoginPasswordVerifier:** creates one random dummy hash at startup. Unknown
  users still undergo password verification; failed requests have a 500 ms floor.
- **AuthService:** issues a 15-minute JWT with identity claims only. Login and
  `/me` fetch the user's memberships from the DB with a bounded query.
- **AuthController:** binds validated DTOs and rejects unknown JSON properties.
  Login errors are generic and authentication responses are marked no-store.
- **Fallback authorization:** every mapped endpoint requires authentication unless
  explicitly anonymous. Only login, `/hello` and `/health` are anonymous here.
- **Login limiter:** five requests per minute per connection IP, no waiting queue,
  then 429 with Retry-After. Untrusted forwarding headers cannot select the IP.
- **Pipeline:** exception handling, routing, authentication, rate limiting,
  authorization, then endpoints. Phase 3 inserts tenant middleware after authentication.
- **Tests:** real bearer validation runs through WebApplicationFactory. Each
  fixture uses random secrets and its own open SQLite in-memory connection.

## 4. How to verify

Use a fresh one-minute rate-limit window before this sequence. More than five
login requests in a minute return 429; obey Retry-After before trying again.
Run commands without PowerShell transcription, curl verbose mode or request logging.

In a second PowerShell terminal:

```powershell
$base = 'http://127.0.0.1:5184'
curl.exe -sS -o NUL -w '%{http_code}\n' "$base/health"
# Expected: 200
curl.exe -sS -o NUL -w '%{http_code}\n' "$base/api/auth/me"
# Expected: 401

$email = Read-Host 'Demo email (see README)'
$securePassword = Read-Host 'Demo password (see README)' -AsSecureString
$password = [System.Net.NetworkCredential]::new('', $securePassword).Password
$body = @{ email = $email; password = $password } | ConvertTo-Json -Compress
$loginJson = $body | curl.exe -sS "$base/api/auth/login" -H 'Content-Type: application/json' --data-binary '@-'
$login = $loginJson | ConvertFrom-Json
if (-not $login.accessToken) { throw 'Login failed; verify credentials and rate-limit window.' }
$token = $login.accessToken
$login.user
$login.tenants
Remove-Variable password, securePassword, body, loginJson, login
# Never display $token.

curl.exe -sS "$base/api/auth/me" -H "Authorization: Bearer $token"
# Expected: current user and only their memberships. No X-Tenant-Id needed yet.

$badBody = @{ email = $email; password = 'incorrect' } | ConvertTo-Json -Compress
$badBody | curl.exe -sS -w '\n%{http_code}\n' "$base/api/auth/login" -H 'Content-Type: application/json' --data-binary '@-'
# Expected: 401, title "Invalid credentials".
$unknownBody = @{ email = 'unknown@tennet.example'; password = 'incorrect' } | ConvertTo-Json -Compress
$unknownBody | curl.exe -sS -w '\n%{http_code}\n' "$base/api/auth/login" -H 'Content-Type: application/json' --data-binary '@-'
# Expected: same status and error shape. Trace IDs differ by request.

$parts = $token.Split('.')
$first = if ($parts[2][0] -eq 'A') { 'B' } else { 'A' }
$parts[2] = $first + $parts[2].Substring(1)
$tampered = $parts -join '.'
curl.exe -sS -o NUL -w '%{http_code}\n' "$base/api/auth/me" -H "Authorization: Bearer $tampered"
# Expected: 401. The signature was actually changed, not just its padding bits.
Remove-Variable parts, tampered
```

For a live expiry check, retain `$token` in this terminal and repeat the following
at least **15 minutes 31 seconds after login**. Do not log in again or replace it:

```powershell
curl.exe -sS -o NUL -w '%{http_code}\n' "$base/api/auth/me" -H "Authorization: Bearer $token"
# Expected: 401 after expiry plus the 30-second clock skew.
Remove-Variable token
```

The automated test uses an already-expired token signed with the real test key,
so expiry can also be verified immediately:

```powershell
dotnet test .\TenNet.sln --no-build --filter FullyQualifiedName~InvalidTokensAreRejected
dotnet test .\TenNet.sln --no-build --filter FullyQualifiedName~LoginIsLimitedToFiveAttempts
dotnet test .\TenNet.sln --no-build --filter FullyQualifiedName~AuthTests
dotnet list .\TenNet.sln package --vulnerable --include-transitive
```

Startup failure with no production key (no secret values are exposed):

```powershell
Push-Location .\Api
dotnet .\bin\Debug\net8.0\TenNet.Api.dll --environment Production --Jwt:SigningKey=
Pop-Location
```

Expected: nonzero exit and the generic key configuration error before listening.
The server-side startup exception is visible in the terminal, never an HTTP response.

### Verified results on 30 September 2026

- [x] Build: zero warnings and errors.
- [x] Full suite: 26 passed, 0 failed, 0 skipped.
- [x] Alice login and `/me`: Acme and Globex; Bob: Initech.
- [x] Live no-token and tampered-token requests: 401.
- [x] Live wrong-password and unknown-email: 401, generic title; 524/513 ms.
- [x] Expired, tampered, wrong-audience, wrong-issuer, wrong-key, future and
      unsigned JWTs: all rejected through the HTTP pipeline in tests.
- [x] Membership removal is reflected by `/me` using the existing token.
- [x] Invalid DTOs rejected; sixth login attempt returns 429 with Retry-After.
- [x] Empty production signing key prevents startup.
- [x] No known vulnerable packages reported by the configured NuGet sources.

## 5. Residual risks

**How could a malicious tenant bypass this?** Authentication identifies a user;
tenant authorization and data isolation are still absent. No tenant-data routes
exist yet. Phase 3 must check membership before setting tenant context; Phase 4
must filter reads and enforce writes even if controller code is wrong.

- The failure timing floor reduces enumeration signals but cannot guarantee
  identical wall-clock times under load, different hash settings or network jitter.
- The limiter is in-memory and per process. Distributed IPs and multiple replicas
  require shared abuse controls; trusted proxy configuration must be explicit.
- Access tokens remain valid until expiry without a revocation mechanism.
- Local HTTP is for loopback development. HTTPS/HSTS and broader hardening remain
  later phases; do not expose these credentials or bearer tokens over public HTTP.
- Membership lists support at most 100 tenants per user; larger accounts get a
  generic server error until a paginated membership response is designed.
- SQL uniqueness is still case-sensitive; current seeded emails are lowercase.
  Any future account creation must enforce a consistent email normalization rule.
- No refresh tokens, password reset, MFA, production account provisioning or
  automatic password-hash upgrades are implemented in this hackathon phase.

Finbuckle remains a reference for tenant resolution in Phase 3. Phase 2 uses
standard ASP.NET Core JWT bearer authentication and the existing membership data.
