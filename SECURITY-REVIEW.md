# Security Review Summary

Review date: 2026-10-03. The full assessment, including diagrams, vulnerable code, fixes, verification steps, the phased remediation plan, and the recommended security tests, is in [docs/SecurityReview.md](docs/SecurityReview.md).

Secrets stored in `appsettings*.json`, `launchSettings.json` and other configuration files are an accepted risk for this private repository and are not reported.

## Totals

| Severity | Count |
|----------|-------|
| 🔴 Critical | 1 |
| 🟠 High | 4 |
| 🟡 Medium | 9 |
| ⚪ Low | 8 |
| ⚪ Informational | 3 |

`dotnet list package --vulnerable --include-transitive` found no known-vulnerable packages.

## Findings

| ID | Severity | Area | Title |
|----|----------|------|-------|
| SEC-001 | 🔴 Critical | Authorization | Anyone can download job packages, which contain the host's connection strings and job source |
| SEC-002 | 🟠 High | Authorization | ViewOnly users can edit, upload, schedule, run and delete jobs, which means running code on the Agent |
| SEC-003 | 🟠 High | Authorization / SSRF | ViewOnly users can read OAuth secrets, send the stored AI API key to any URL, and change Community/MCP endpoints |
| SEC-004 | 🟠 High | Authentication | External login links accounts by an unverified email on the multi-tenant Microsoft endpoint |
| SEC-005 | 🟠 High | Authentication / Configuration | The anonymous setup wizard fails open: it exposes connection strings, rewrites appsettings and can create an Admin |
| SEC-006 | 🟡 Medium | Authentication | A rejected external login leaves an authenticated cookie |
| SEC-007 | 🟡 Medium | Authentication | No account lockout or brute-force protection |
| SEC-008 | 🟡 Medium | Authentication | 30-day sliding cookie without revalidation; `SecurePolicy.SameAsRequest` |
| SEC-009 | 🟡 Medium | HTTP Security | No security headers (CSP, X-Frame-Options, nosniff, Referrer-Policy, Permissions-Policy) |
| SEC-010 | 🟡 Medium | Logging | Webhook bearer GUID and full payloads are written to logs |
| SEC-011 | 🟡 Medium | Rate Limiting / DoS | No rate limiting or body-size limits on the webhook and login endpoints |
| SEC-012 | 🟡 Medium | Supply Chain | Floating NuGet versions (`*`, `1.*`) |
| SEC-013 | 🟡 Medium | Hosting | The Agent container runs job code as root with no isolation |
| SEC-014 | 🟡 Medium | Injection (Path Traversal) | The NuGet resolver builds file paths without containment checks |
| SEC-015 | ⚪ Low | Authorization | Anonymous `BuildErrorsController` discloses internal telemetry |
| SEC-016 | ⚪ Low | CSRF | Login skips antiforgery (`[IgnoreAntiforgeryToken]`) and logout accepts GET |
| SEC-017 | ⚪ Low | Data Protection | Data Protection keys are not persisted |
| SEC-018 | ⚪ Low | Secrets in Source | A SQL `sa` password literal is hard-coded in C# |
| SEC-019 | ⚪ Low | Error Handling | Raw exception messages are returned to anonymous clients |
| SEC-020 | ⚪ Low | Logging | Insufficient audit trail for security-relevant actions |
| SEC-021 | ⚪ Low | HTTP Security | Forwarded headers are not explicitly configured |
| SEC-022 | ⚪ Low | DoS / Uploads | 100 MB uploads are buffered in memory; circuit limits are not tuned |
| SEC-023 | ⚪ Info | Configuration | Health endpoints are public in all environments |
| SEC-024 | ⚪ Info | Supply Chain | Runtime projects reference Aspire hosting packages |
| SEC-025 | ⚪ Info | Injection (Prompt) | Untrusted content reaches LLM prompts and job code |

## Immediate actions

1. Put `[Authorize(Roles = "Admin")]` on `JobPackageController`. Stop stamping host connection strings into stored packages, re-stamp the existing blobs, and **rotate SQL and storage credentials** (SEC-001).
2. Enforce the Admin role on the server for job editing and execution, and for the `/admin` tabs (SEC-002, SEC-003).
3. Stop auto-linking external identities by email, or restrict Microsoft login to one tenant. Use a separate external cookie that is always cleared (SEC-004, SEC-006).
4. Make `/setup` fail closed once installed, never pre-fill connection strings, and create the first admin atomically (SEC-005).
