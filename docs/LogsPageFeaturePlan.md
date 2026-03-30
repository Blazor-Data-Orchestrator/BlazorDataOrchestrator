# Logs Page Feature Plan

## Overview
Add a dedicated **Logs** page accessible from the left sidebar navigation, positioned between **Dashboard** and **Administration**. This page provides a consolidated, at-a-glance view of all job activity — recent execution logs, upcoming scheduled runs, and current job statuses — without overwhelming the user with detail. Users can drill down into full logs or full schedules on demand.

## Goals
- Provide a single place to monitor all job activity across the system
- Show one summary row per job (not detailed logs) by default
- Display next scheduled run per job (not the full schedule) by default
- Allow drill-down into detailed logs and full schedule on user action
- Match the existing UI design language (Radzen components, card styles, color palette)

---

## UI Reference

The existing dashboard uses:
- **Radzen Blazor** components (`RadzenDataGrid`, `RadzenCard`, `RadzenBadge`, `RadzenButton`, `RadzenIcon`, `RadzenStack`, `RadzenTabs`)
- Color palette: `#111827` (headings), `#374151` (body), `#6b7280` (muted), `#9ca3af` (captions), `#e5e7eb` (borders), `#f3f4f6` (active nav), `#16a34a` (success), `#ef4444` (error), `#eab308` (warning/queued)
- Card style: `border: 1px solid #e5e7eb; border-radius: 12px; box-shadow: 0 1px 3px rgba(0,0,0,0.06);`
- Navigation icons use inline SVG data URIs in CSS

---

## Implementation Tasks

### Task 1: Add "Logs" Nav Item to Sidebar

**File:** `src/BlazorOrchestrator.Web/Components/Layout/NavMenu.razor`

Add a new nav item between **Dashboard** and **Administration** in the `else` block:

```razor
<div class="nav-item px-3">
    <NavLink class="nav-link" href="logs">
        <span class="bi bi-journal-text-nav-menu" aria-hidden="true"></span> Logs
    </NavLink>
</div>
```

**File:** `src/BlazorOrchestrator.Web/Components/Layout/NavMenu.razor.css`

Add a new icon class using a journal/list icon SVG (matching existing pattern):

```css
.bi-journal-text-nav-menu {
    background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='16' height='16' fill='%23374151' class='bi bi-journal-text' viewBox='0 0 16 16'%3E%3Cpath d='M5 10.5a.5.5 0 0 1 .5-.5h2a.5.5 0 0 1 0 1h-2a.5.5 0 0 1-.5-.5zm0-2a.5.5 0 0 1 .5-.5h5a.5.5 0 0 1 0 1h-5a.5.5 0 0 1-.5-.5zm0-2a.5.5 0 0 1 .5-.5h5a.5.5 0 0 1 0 1h-5a.5.5 0 0 1-.5-.5zm0-2a.5.5 0 0 1 .5-.5h5a.5.5 0 0 1 0 1h-5a.5.5 0 0 1-.5-.5z'/%3E%3Cpath d='M3 0h10a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2v-1h1v1a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1V2a1 1 0 0 0-1-1H3a1 1 0 0 0-1 1v1H1V2a2 2 0 0 1 2-2z'/%3E%3Cpath d='M1 5v-.5a.5.5 0 0 1 1 0V5h.5a.5.5 0 0 1 0 1h-2a.5.5 0 0 1 0-1H1zm0 3v-.5a.5.5 0 0 1 1 0V8h.5a.5.5 0 0 1 0 1h-2a.5.5 0 0 1 0-1H1zm0 3v-.5a.5.5 0 0 1 1 0v.5h.5a.5.5 0 0 1 0 1h-2a.5.5 0 0 1 0-1H1z'/%3E%3C/svg%3E");
}
```

---

### Task 2: Create the Logs Page Component

**File:** `src/BlazorOrchestrator.Web/Components/Pages/Logs.razor` (new)

#### Page Route & Authorization
```razor
@page "/logs"
@attribute [Authorize]
```

#### Page Header
Matching the dashboard style:
```
📋 Job Logs & Upcoming Runs
  View recent activity, upcoming schedules, and execution history for all jobs.
```

Use the same `RadzenStack` + `RadzenIcon` + `RadzenText` pattern from `Home.razor` header.

#### Page Layout — Three Sections via Tabs

Use `RadzenTabs` with three tabs:

| Tab | Icon | Description |
|-----|------|-------------|
| **Recent Activity** | `history` | Summary of latest execution per job |
| **Upcoming Runs** | `schedule` | Next scheduled run per job |
| **All Logs** | `list_alt` | Searchable/filterable full log view |

---

### Task 3: "Recent Activity" Tab (Default)

Displays a `RadzenDataGrid` with **one row per job** showing the most recent execution only.

#### Columns

| Column | Source | Description |
|--------|--------|-------------|
| Job Name | `Job.JobName` | Link/button to expand details |
| Status | `JobInstance` | Badge: Success (green), Error (red), In Progress (blue), Queued (yellow) |
| Last Run | `JobSchedule.LastRun` | Formatted with `TimeDisplayService` |
| Duration | Computed | Difference between `CreatedDate` and `UpdatedDate` of the latest `JobInstance` |
| Message | Azure Table `JobLogs` | Latest log entry summary (truncated to ~80 chars) |
| Actions | — | "View Details" button |

#### "View Details" Expand/Drill-Down
When user clicks "View Details" on a row:
- Open a dialog (reuse the existing `OpenLogsDialog` pattern from `Home.razor`) or expand an inline detail panel
- Show the full list of `JobInstance` records and their Table Storage logs for that job
- Use the existing `LogDisplayEntry` model and `RadzenDataGrid` pattern already in `Home.razor`

#### Data Source
- Query all jobs with their latest `JobInstance` (via `JobService`)
- For each job, fetch the latest single log entry from Azure Table Storage (via `JobManager.GetAllLogsForJobAsync` with `maxResults: 1`)
- Add a new service method (see Task 5) to do this efficiently

---

### Task 4: "Upcoming Runs" Tab

Displays a `RadzenDataGrid` with **one row per job** showing only the next scheduled run.

#### Columns

| Column | Source | Description |
|--------|--------|-------------|
| Job Name | `Job.JobName` | Job identifier |
| Status | `Job.JobEnabled` | Badge: Enabled (green) / Disabled (grey) |
| Next Run | Computed from `JobSchedule` | The next calculated run time |
| Schedule Summary | `JobSchedule` | Human-readable text, e.g. "Every 2 hours, Mon–Fri" |
| Actions | — | "View Full Schedule" button |

#### "View Full Schedule" Drill-Down
When user clicks "View Full Schedule":
- Open a dialog showing all `JobSchedule` records for that job
- Display: schedule name, enabled/disabled, day-of-week flags, start/stop times, run interval, last run
- Reuse the existing `OpenJobScheduleDialog` pattern from `Home.razor`

#### Data Source
- Query all jobs with their `JobSchedule` collection
- Compute next run using the schedule fields (`RunEveryHour`, `StartTime`, `StopTime`, day-of-week booleans, `LastRun`)
- Add a helper method to compute next run time (see Task 5)

---

### Task 5: "All Logs" Tab

A searchable, paginated view of all log entries across all jobs.

#### Columns

| Column | Source | Description |
|--------|--------|-------------|
| Timestamp | `JobLogs` table | When the log was created |
| Job Name | Joined from `Job` | Which job produced this log |
| Level | `JobLogs.Level` | Badge: Info (blue), Warning (yellow), Error (red) |
| Action | `JobLogs.Action` | `JobProgress` / `JobError` |
| Message | `JobLogs.Details` | Full log message (expandable if long) |
| Instance ID | `JobLogs.JobInstanceId` | Link to filter by instance |

#### Features
- **Search box**: Filter logs by job name or message text (client-side or server-side)
- **Level filter**: Dropdown to filter by Info / Warning / Error
- **Job filter**: Dropdown to filter by specific job
- **Pagination**: 20 rows per page (server-side via Table Storage continuation tokens or query limits)
- **Auto-refresh toggle**: Optional button to poll for new logs every 30 seconds

#### Data Source
- Query Azure Table Storage `JobLogs` table across all partition keys
- Join with SQL `Job` table for job names
- New service method needed (see Task 6)

---

### Task 6: New Service Methods

**File:** `src/BlazorOrchestrator.Web/Services/JobService.cs`

Add the following methods:

```csharp
/// <summary>
/// Gets a summary of the latest execution for each job (one row per job).
/// </summary>
Task<List<JobActivitySummary>> GetRecentActivitySummaryAsync(int? organizationId = null);

/// <summary>
/// Gets the next scheduled run for each job (one row per job).
/// </summary>
Task<List<JobUpcomingRun>> GetUpcomingRunsAsync(int? organizationId = null);
```

**File:** `src/BlazorDataOrchestrator.Core/JobManager.cs`

Add or expose:

```csharp
/// <summary>
/// Gets the latest N log entries across all jobs, for the "All Logs" tab.
/// </summary>
Task<List<JobLogEntry>> GetRecentLogsAsync(int maxResults = 100);

/// <summary>
/// Gets the latest single log entry for a specific job (for summary view).
/// </summary>
Task<JobLogEntry?> GetLatestLogForJobAsync(int jobId);
```

---

### Task 7: New View Models

**File:** `src/BlazorOrchestrator.Web/Components/Pages/Logs.razor` (inside `@code` block, matching `Home.razor` pattern)

```csharp
private class JobActivitySummary
{
    public int JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;       // "Success", "Error", "InProgress", "Queued"
    public string LastRun { get; set; } = string.Empty;       // Formatted timestamp
    public string Duration { get; set; } = string.Empty;      // e.g. "2m 15s"
    public string LatestMessage { get; set; } = string.Empty;  // Truncated log message
    public bool HasError { get; set; }
    public bool IsRunning { get; set; }
}

private class JobUpcomingRun
{
    public int JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string NextRun { get; set; } = string.Empty;        // Formatted timestamp
    public string ScheduleSummary { get; set; } = string.Empty; // e.g. "Every 2h, Mon-Fri, 8AM-6PM"
    public int ScheduleCount { get; set; }                      // Number of schedules for this job
}

private class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string JobName { get; set; } = string.Empty;
    public int JobId { get; set; }
    public int JobInstanceId { get; set; }
    public string Level { get; set; } = string.Empty;          // "Info", "Warning", "Error"
    public string Action { get; set; } = string.Empty;         // "JobProgress", "JobError"
    public string Message { get; set; } = string.Empty;
}
```

---

### Task 8: Next-Run Calculation Helper

Add a utility method (in `JobService` or a new `ScheduleHelper` class) to compute the next scheduled run time from `JobSchedule` fields:

**Input:** `JobSchedule` (day-of-week booleans, `RunEveryHour`, `StartTime`, `StopTime`, `LastRun`)  
**Output:** `DateTime?` — the next time this schedule would fire

**Logic:**
1. If schedule is disabled → return `null`
2. Start from `LastRun` (or `DateTime.UtcNow` if never run)
3. Add `RunEveryHour` hours
4. Check if result falls within `StartTime`–`StopTime` window
5. Check if result falls on an enabled day-of-week
6. If not, advance to next valid day/time window

Generate a human-readable schedule summary string, e.g.:
- "Every 2 hours, Mon–Fri, 8:00 AM – 6:00 PM"
- "Every 1 hour, Daily"
- "Every 4 hours, Weekdays only"

---

## File Summary

| File | Action |
|------|--------|
| `src/BlazorOrchestrator.Web/Components/Layout/NavMenu.razor` | Add Logs nav item |
| `src/BlazorOrchestrator.Web/Components/Layout/NavMenu.razor.css` | Add journal icon CSS class |
| `src/BlazorOrchestrator.Web/Components/Pages/Logs.razor` | **New** — main Logs page |
| `src/BlazorOrchestrator.Web/Components/Pages/Logs.razor.css` | **New** — page-specific styles (minimal) |
| `src/BlazorOrchestrator.Web/Services/JobService.cs` | Add summary query methods |
| `src/BlazorDataOrchestrator.Core/JobManager.cs` | Add log retrieval methods |

---

## UI Wireframe (Text)

```
┌─────────────────────────────────────────────────────────────────────────┐
│  BLAZOR DATA ORCHESTRATOR                              admin  🔔  Sign out │
├──────────┬──────────────────────────────────────────────────────────────┤
│ MAIN     │  📋 Job Logs & Upcoming Runs                        🔄 Refresh │
│          │  View recent activity, upcoming schedules, and              │
│ Dashboard│  execution history for all jobs.                            │
│ Logs ◄── │                                                             │
│ Admin    │  ┌──────────────┬──────────────┬──────────────┐             │
│          │  │Recent Activity│ Upcoming Runs│  All Logs    │             │
│          │  └──────────────┴──────────────┴──────────────┘             │
│          │                                                             │
│          │  ┌─────────┬────────┬──────────┬─────────┬─────────┬──────┐│
│          │  │Job Name │ Status │ Last Run │Duration │ Message │Action││
│          │  ├─────────┼────────┼──────────┼─────────┼─────────┼──────┤│
│          │  │TestJob  │🟢 OK  │ 5m ago   │ 2m 15s  │ Job co… │Detail││
│          │  │TestJob2 │🟡 Queue│ Never    │ —       │ —       │Detail││
│          │  │ImportJob│🔴 Error│ 1h ago   │ 0m 03s  │ Connec… │Detail││
│          │  └─────────┴────────┴──────────┴─────────┴─────────┴──────┘│
│          │                                          Page 1 of 1        │
└──────────┴──────────────────────────────────────────────────────────────┘
```

---

## Design Decisions

1. **One row per job (summary view)** — Avoids information overload. Detailed logs are one click away via existing dialog patterns.
2. **Tabs instead of separate pages** — Keeps all log-related info in one place. Matches the `RadzenTabs` pattern already used in `AdminHome.razor`.
3. **Reuse existing dialogs** — The logs detail drill-down reuses the `OpenLogsDialog` pattern from `Home.razor`. Schedule drill-down reuses `OpenJobScheduleDialog`.
4. **Azure Table Storage for logs** — Continues the existing pattern where `JobManager` writes/reads from the `JobLogs` table.
5. **SQL for job metadata** — Job names, schedules, instances come from SQL via `JobService`/EF Core, consistent with existing architecture.

---

## Dependencies
- No new NuGet packages required
- Uses existing Radzen Blazor components
- Uses existing `JobService`, `JobManager`, `TimeDisplayService` services
- Uses existing `ApplicationDbContext` and Azure Table Storage infrastructure

## Estimated Scope
- **New files:** 2 (Logs.razor, Logs.razor.css)
- **Modified files:** 4 (NavMenu.razor, NavMenu.razor.css, JobService.cs, JobManager.cs)
- **New view models:** 3 (defined within Logs.razor)
- **New service methods:** ~4
