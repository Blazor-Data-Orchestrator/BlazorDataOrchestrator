# Authentication Flash Fix Plan

## Problem Statement

When a user navigates to the application, the **home page with its menu and layout briefly renders** before the login screen appears. This "flash of unauthenticated content" (FOUC) occurs because:

1. `Home.razor` is marked `[AllowAnonymous]` — so `AuthorizeRouteView` never blocks it.
2. The page performs an async `CheckSystemStatus()` database call **before** checking authentication state.
3. Only after the async system check completes does the page evaluate auth and trigger a full-page `forceLoad: true` redirect to `/account/login`.
4. `Routes.razor` has no `<Authorizing>` template, so there is no placeholder while the auth state resolves.

The user sees the MainLayout chrome (sidebar, top bar) and a loading spinner for a visible period before being redirected.

## Current Architecture

```mermaid
sequenceDiagram
    participant Browser
    participant Server as Blazor Server
    participant Home as Home.razor
    participant DB as Database
    participant Auth as AuthState

    Browser->>Server: GET /
    Server->>Browser: Render App.razor shell
    Browser->>Server: Establish SignalR circuit
    Server->>Home: Render (AllowAnonymous - no gate)
    Home->>Home: IsLoading = true, render MainLayout + spinner
    Home->>DB: CheckSystemStatus() (async)
    DB-->>Home: System OK
    Home->>Auth: await AuthStateTask
    Auth-->>Home: Not authenticated
    Home->>Home: ShouldRedirectToLogin = true
    Home->>Browser: RedirectToLogin component renders
    Browser->>Server: forceLoad navigation to /account/login
    Server->>Browser: Login page
```

**Problem window**: Between "Render MainLayout + spinner" and the final redirect, the user sees a flash of the authenticated layout.

## Desired Behavior

```mermaid
sequenceDiagram
    participant Browser
    participant Server as Blazor Server
    participant Routes as Routes.razor
    participant Auth as AuthState
    participant Home as Home.razor

    Browser->>Server: GET /
    Server->>Browser: Render App.razor shell
    Browser->>Server: Establish SignalR circuit
    Server->>Routes: Router resolves route
    Routes->>Auth: Resolve auth state
    Note over Routes: Show blank/minimal Authorizing template
    Auth-->>Routes: Not authenticated
    Routes->>Browser: RedirectToLogin (immediate)
    Browser->>Server: Navigate to /account/login
    Server->>Browser: Login page (no flash)
```

## Root Cause Analysis

```mermaid
flowchart TD
    A["User hits /"] --> B{"AuthorizeRouteView checks page attribute"}
    B -->|"AllowAnonymous"| C["Page renders immediately"]
    B -->|"Authorize"| D["Waits for auth state"]
    C --> E["MainLayout renders with sidebar and menu"]
    E --> F["OnInitializedAsync runs"]
    F --> G["await CheckSystemStatus - DB call"]
    G --> H{"Is authenticated?"}
    H -->|No| I["Set ShouldRedirectToLogin = true"]
    I --> J["Re-render with RedirectToLogin"]
    J --> K["forceLoad: true - full page redirect"]
    K --> L["Login page finally appears"]
    H -->|Yes| M["Load dashboard"]

    style C fill:#ffcccc,stroke:#cc0000
    style E fill:#ffcccc,stroke:#cc0000
    style G fill:#ffcccc,stroke:#cc0000
    note1["Flash visible during C through K"]

    D --> N{"Auth resolved?"}
    N -->|"Not auth"| O["NotAuthorized template"]
    N -->|"Resolving"| P["Authorizing template (currently missing)"]
```

## Solution Architecture

The fix involves three coordinated changes:

```mermaid
flowchart TD
    subgraph Fix1["Fix 1: Remove AllowAnonymous from Home"]
        A1["Home.razor gets Authorize attribute"]
        A2["AuthorizeRouteView gates access before render"]
    end

    subgraph Fix2["Fix 2: Add Authorizing template to Routes"]
        B1["Routes.razor defines Authorizing template"]
        B2["Blank or minimal spinner shown while resolving"]
        B3["No layout chrome leaks through"]
    end

    subgraph Fix3["Fix 3: Separate Install Wizard route"]
        C1["New /setup route with AllowAnonymous"]
        C2["Install logic moved out of Home.razor"]
        C3["Home.razor is purely the authenticated dashboard"]
    end

    Fix1 --> Result["No flash - auth enforced at router level"]
    Fix2 --> Result
    Fix3 --> Result
```

## Detailed Implementation Plan

### Step 1: Add Authorizing Template to Routes.razor

The `<Authorizing>` template ensures nothing renders while the `AuthenticationState` is being resolved asynchronously.

**File**: `Components/Routes.razor`

**Current:**
```razor
<Router AppAssembly="typeof(Program).Assembly">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
            <NotAuthorized>
                <RedirectToLogin />
            </NotAuthorized>
        </AuthorizeRouteView>
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

**Updated:**
```razor
<Router AppAssembly="typeof(Program).Assembly">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
            <Authorizing>
                <div class="auth-loading-container">
                    <div class="spinner-border text-primary" role="status">
                        <span class="visually-hidden">Loading...</span>
                    </div>
                </div>
            </Authorizing>
            <NotAuthorized>
                <RedirectToLogin />
            </NotAuthorized>
        </AuthorizeRouteView>
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

**Key**: The `<Authorizing>` template renders **outside** `MainLayout`, so no sidebar or nav will appear during auth resolution.

### Step 2: Create a Dedicated Setup/Install Route

The reason `Home.razor` is `[AllowAnonymous]` is that it doubles as the install wizard entry point. Separating these concerns eliminates the conflict.

**New File**: `Components/Pages/Setup.razor`

```razor
@page "/setup"
@attribute [AllowAnonymous]
@layout LoginLayout
```

This page handles:
- Database connection check
- Initial table creation
- Admin user creation

It uses `LoginLayout` (minimal, no nav chrome) so even if accessed directly, there is no flash of the full dashboard layout.

### Step 3: Change Home.razor to Authorize-Only

**File**: `Components/Pages/Home.razor`

**Change:**
```razor
@page "/"
@attribute [AllowAnonymous]   ← REMOVE THIS
```

**To:**
```razor
@page "/"
@attribute [Authorize]
```

With `[Authorize]`, the `AuthorizeRouteView` in `Routes.razor` will:
1. Show the `<Authorizing>` template while auth state resolves
2. Immediately render `<NotAuthorized>` → `<RedirectToLogin />` if unauthenticated
3. Only render `Home.razor` content if authenticated

**No async system check runs for unauthenticated users** — the redirect happens at the router level before the page component even instantiates.

### Step 4: Add Startup Redirect Logic for Unconfigured Systems

Since `/` is now `[Authorize]`, a fresh install (no DB, no users) would be stuck in a redirect loop. Add middleware to detect this and redirect to `/setup`.

**File**: `Program.cs`

Add a lightweight middleware **before** `UseAuthentication()`:

```csharp
// Redirect to setup if system is not configured
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/setup") &&
        !context.Request.Path.StartsWithSegments("/account") &&
        !context.Request.Path.StartsWithSegments("/_blazor") &&
        !context.Request.Path.StartsWithSegments("/_framework"))
    {
        var systemCheck = context.RequestServices.GetService<ISystemStatusService>();
        if (systemCheck != null && !await systemCheck.IsConfiguredAsync())
        {
            context.Response.Redirect("/setup");
            return;
        }
    }
    await next();
});
```

### Step 5: Create ISystemStatusService

**New File**: `Services/ISystemStatusService.cs`

```csharp
public interface ISystemStatusService
{
    Task<bool> IsConfiguredAsync();
}
```

**New File**: `Services/SystemStatusService.cs`

Caches the "is configured" result so the middleware check is near-zero cost after first evaluation:

```csharp
public class SystemStatusService : ISystemStatusService
{
    private bool? _isConfigured;
    private readonly IServiceProvider _serviceProvider;

    public SystemStatusService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<bool> IsConfiguredAsync()
    {
        if (_isConfigured.HasValue)
            return _isConfigured.Value;

        // Check if database is reachable and has tables/admin
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetService<ApplicationDbContext>();
        if (dbContext == null)
        {
            _isConfigured = false;
            return false;
        }

        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync();
            if (!canConnect)
            {
                _isConfigured = false;
                return false;
            }

            // Check if admin user exists (system is configured)
            var hasAdmin = await dbContext.Users.AnyAsync();
            _isConfigured = hasAdmin;
            return hasAdmin;
        }
        catch
        {
            _isConfigured = false;
            return false;
        }
    }
}
```

Register as singleton so the cached result persists for the lifetime of the app:

```csharp
builder.Services.AddSingleton<ISystemStatusService, SystemStatusService>();
```

### Step 6: Ensure MainLayout Does Not Render for Unauthenticated Users

The `AuthorizeRouteView` with `DefaultLayout` only applies the layout **after** authorization succeeds. However, verify that `MainLayout.razor` wraps its nav chrome in an auth check:

**File**: `Components/Layout/MainLayout.razor`

Ensure the sidebar and top bar are inside:
```razor
<AuthorizeView>
    <Authorized>
        <!-- Sidebar, nav menu, top bar -->
        <div class="page">
            <div class="sidebar">
                <NavMenu />
            </div>
            <main>
                @Body
            </main>
        </div>
    </Authorized>
    <NotAuthorized>
        @Body
    </NotAuthorized>
</AuthorizeView>
```

This provides a fallback: even if somehow the layout renders for an unauthenticated user, they see only `@Body` (no nav chrome).

### Step 7: Style the Auth Loading State

**File**: `wwwroot/css/app.css` (or equivalent)

```css
.auth-loading-container {
    display: flex;
    justify-content: center;
    align-items: center;
    height: 100vh;
    background-color: var(--bs-body-bg, #fff);
}
```

This ensures the loading state is a clean full-screen spinner with no layout artifacts.

## Component Responsibility After Refactor

```mermaid
flowchart TD
    subgraph Router["Routes.razor (Router Level)"]
        R1{"Page has [Authorize]?"}
        R1 -->|Yes| R2{"Auth state resolved?"}
        R2 -->|Resolving| R3["Show Authorizing template (spinner)"]
        R2 -->|Authenticated| R4["Render page with MainLayout"]
        R2 -->|Not authenticated| R5["RedirectToLogin"]
        R1 -->|"AllowAnonymous"| R6["Render page directly"]
    end

    subgraph Pages["Page Components"]
        P1["Home.razor - Authorize - Dashboard only"]
        P2["Setup.razor - AllowAnonymous - Install wizard"]
        P3["Login.razor - AllowAnonymous - LoginLayout"]
    end

    subgraph Middleware["Program.cs Middleware"]
        M1{"System configured?"}
        M1 -->|No| M2["Redirect to /setup"]
        M1 -->|Yes| M3["Continue pipeline"]
    end
```

## Complete Request Flow After Fix

```mermaid
sequenceDiagram
    participant Browser
    participant MW as Middleware
    participant Router as Routes.razor
    participant Auth as AuthState
    participant Login as Login Page

    Browser->>MW: GET /
    MW->>MW: IsConfigured? (cached - fast)
    
    alt System not configured
        MW->>Browser: 302 Redirect /setup
        Browser->>MW: GET /setup
        MW->>Router: AllowAnonymous page
        Router->>Browser: Render Setup wizard (LoginLayout)
    else System configured
        MW->>Router: Continue to Blazor
        Router->>Router: Resolve route to Home.razor
        Router->>Router: Home has [Authorize]
        Router->>Auth: Get authentication state
        
        alt Auth resolving
            Router->>Browser: Render Authorizing template (spinner only)
        end
        
        Auth-->>Router: State resolved
        
        alt Not authenticated
            Router->>Browser: RedirectToLogin
            Browser->>MW: GET /account/login
            MW->>Router: AllowAnonymous page
            Router->>Browser: Render Login page (LoginLayout)
        else Authenticated
            Router->>Browser: Render Home.razor with MainLayout
        end
    end
```

## Migration Strategy

Since this changes the fundamental routing of the application, the migration must be done carefully:

```mermaid
flowchart LR
    subgraph Phase1["Phase 1: Non-Breaking Prep"]
        A["Add Authorizing template to Routes.razor"]
        B["Create ISystemStatusService"]
        C["Create Setup.razor page"]
        D["Add auth-loading-container CSS"]
    end

    subgraph Phase2["Phase 2: Move Install Logic"]
        E["Extract install wizard logic from Home.razor"]
        F["Move to Setup.razor"]
        G["Add middleware redirect for unconfigured systems"]
    end

    subgraph Phase3["Phase 3: Lock Down Home"]
        H["Change Home.razor from AllowAnonymous to Authorize"]
        I["Remove manual auth check from OnInitializedAsync"]
        J["Remove ShouldRedirectToLogin logic"]
        K["Simplify Home.razor to dashboard-only"]
    end

    subgraph Phase4["Phase 4: Verify"]
        L["Test fresh install flow via /setup"]
        M["Test unauthenticated access - no flash"]
        N["Test authenticated access - immediate dashboard"]
        O["Test session expiry - clean redirect"]
    end

    Phase1 --> Phase2 --> Phase3 --> Phase4
```

## File Changes Summary

| File | Change |
|---|---|
| `Components/Routes.razor` | Add `<Authorizing>` template with minimal spinner |
| `Components/Pages/Home.razor` | Change `[AllowAnonymous]` to `[Authorize]`; remove manual auth checks |
| `Components/Pages/Setup.razor` | **New** — Install wizard with `[AllowAnonymous]` and `LoginLayout` |
| `Components/Layout/MainLayout.razor` | Wrap nav chrome in `<AuthorizeView>` guard |
| `Program.cs` | Add `ISystemStatusService` registration; add middleware for unconfigured redirect |
| `Services/ISystemStatusService.cs` | **New** — Interface for system configuration check |
| `Services/SystemStatusService.cs` | **New** — Cached implementation |
| `wwwroot/css/app.css` | Add `.auth-loading-container` styles |

## Testing Scenarios

| Scenario | Expected Behavior |
|---|---|
| Fresh install, no DB | Middleware redirects to `/setup` immediately |
| Configured system, unauthenticated user | Spinner briefly, then login page (no nav flash) |
| Configured system, authenticated user | Dashboard renders directly |
| Session expires mid-use | Next navigation shows spinner then login |
| Direct URL to protected page (e.g. `/jobs`) | Spinner then login, returnUrl preserved |
| Direct URL to `/setup` when configured | Setup page loads (harmless, shows "already configured") |

## Key Principles

1. **Authentication enforcement belongs at the router level**, not inside individual page components.
2. **The `<Authorizing>` template prevents any layout chrome from rendering** during the async auth state resolution window.
3. **Separate concerns**: Install wizard and dashboard are different responsibilities requiring different auth policies — they should be different routes.
4. **Middleware handles the edge case** of a completely unconfigured system needing anonymous access to set up.
5. **Cache the system status check** so it adds negligible overhead to every request after the first evaluation.
