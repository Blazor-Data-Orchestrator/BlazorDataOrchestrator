# Implementation Plan: Timezone Setting Feature

## Overview
Add a time zone configuration feature to the Administration page, storing the offset in `appsettings.json` and applying it to all log display pages.

---

## Phase 1: Configuration Infrastructure

### 1.1 Update `appsettings.json`
**File:** `src/BlazorOrchestrator.Web/appsettings.json`

Add the new configuration key:
```json
{
  "TimezoneOffset": "-08:00",
  ...existing settings...
}
```

### 1.2 Create a Settings Service
**New File:** `src/BlazorOrchestrator.Web/Services/AppSettingsService.cs`

Create a service to read/write `appsettings.json`:
- Method: `GetTimezoneOffset(): TimeSpan` - Returns the configured timezone offset
- Method: `SetTimezoneOffset(TimeSpan offset): Task` - Saves the offset to appsettings.json
- Method: `GetAvailableTimezones(): List<(string Label, string Value)>` - Returns common timezone offsets for dropdown

### 1.3 Register the Service
**File:** `src/BlazorOrchestrator.Web/Program.cs`

Register `AppSettingsService` as a singleton (since it manages file-based configuration).

---

## Phase 2: Administration UI

### 2.1 Add Settings Tab to AdminHome.razor
**File:** `src/BlazorOrchestrator.Web/Components/Pages/Admin/AdminHome.razor`

Add a new **"Settings"** tab to the existing `RadzenTabs`:
- Display current timezone offset
- Dropdown to select from common timezone offsets (UTC-12 to UTC+14)
- Custom input option for specific offsets
- Save button to persist the setting
- Show a preview of how times will be displayed

**UI Components:**
```
Settings Tab
├── RadzenAlert (info about timezone setting)
├── RadzenDropDown (timezone selection)
│   ├── UTC-12:00 (Baker Island)
│   ├── UTC-08:00 (Pacific Time) [DEFAULT]
│   ├── UTC-05:00 (Eastern Time)
│   ├── UTC+00:00 (UTC)
│   ├── UTC+05:30 (India)
│   └── ...more options
├── RadzenText (current time preview)
└── RadzenButton (Save)
```

---

## Phase 3: Time Display Helper

### 3.1 Create Time Display Helper
**New File:** `src/BlazorOrchestrator.Web/Services/TimeDisplayService.cs`

Create a service to format DateTime values with the configured offset:
- Method: `FormatDateTime(DateTime utcTime, string format = "g"): string`
- Method: `ConvertToDisplayTime(DateTime utcTime): DateTime`
- Inject `IConfiguration` to read the current offset

---

## Phase 4: Update Log Display Pages

### 4.1 Update TEMPORARY_HomePage.razor (Logs Dialog)
**File:** `src/BlazorOrchestrator.Web/Components/Pages/TEMPORARY/TEMPORARY_HomePage.razor`

**Lines to update:**
- Line ~417: `@log.CreatedDate.ToString("g")` → Use `TimeDisplayService.FormatDateTime(log.CreatedDate)`

**Changes needed:**
1. Inject `TimeDisplayService`
2. Update the log grid column template to use the service

### 4.2 Update JobDetailsDialog.razor (Logs Tab)
**File:** `src/BlazorOrchestrator.Web/Components/Pages/Dialogs/JobDetailsDialog.razor`

**Lines to update:**
- Line ~168: `@log.CreatedDate.ToString("g")` → Use `TimeDisplayService.FormatDateTime(log.CreatedDate)`
- Line ~144: `@(param.JobDateValue?.ToString("g") ?? "-")` → Use service for date parameter display

**Changes needed:**
1. Inject `TimeDisplayService`
2. Update the log grid column template
3. Update the parameters grid date column template

### 4.3 Update AdminHome.razor (CreatedDate displays)
**File:** `src/BlazorOrchestrator.Web/Components/Pages/Admin/AdminHome.razor`

**Lines to update:**
- Line ~79: `@group.CreatedDate.ToString("g")` (Job Groups grid)
- Line ~144: `@queue.CreatedDate.ToString("g")` (Job Queues grid)

**Changes needed:**
1. Inject `TimeDisplayService`
2. Update both grid column templates

---

## Implementation Order

| Step | Task | Estimated Files |
|------|------|-----------------|
| 1 | Add `TimezoneOffset` to `appsettings.json` | 1 |
| 2 | Create `AppSettingsService.cs` | 1 |
| 3 | Create `TimeDisplayService.cs` | 1 |
| 4 | Register services in `Program.cs` | 1 |
| 5 | Add Settings tab to `AdminHome.razor` | 1 |
| 6 | Update `TEMPORARY_HomePage.razor` log display | 1 |
| 7 | Update `JobDetailsDialog.razor` time displays | 1 |
| 8 | Update `AdminHome.razor` grid time displays | 1 |

---

## Technical Considerations

1. **DateTime Storage**: Ensure all stored dates are in UTC. The timezone offset is only for display purposes.

2. **IConfiguration Reload**: When saving to `appsettings.json`, trigger a configuration reload so all services see the new value immediately.

3. **Cascading Updates**: Consider using a `CascadingValue` or event to notify all open pages when the timezone changes, refreshing displayed times.

4. **Validation**: Validate timezone offset format (e.g., `-08:00`, `+05:30`) before saving.

5. **Default Fallback**: If the setting is missing or invalid, default to `-08:00` (Pacific Time).

---

## Files to Create
1. `src/BlazorOrchestrator.Web/Services/AppSettingsService.cs`
2. `src/BlazorOrchestrator.Web/Services/TimeDisplayService.cs`

## Files to Modify
1. `src/BlazorOrchestrator.Web/appsettings.json`
2. `src/BlazorOrchestrator.Web/Program.cs`
3. `src/BlazorOrchestrator.Web/Components/Pages/Admin/AdminHome.razor`
4. `src/BlazorOrchestrator.Web/Components/Pages/TEMPORARY/TEMPORARY_HomePage.razor`
5. `src/BlazorOrchestrator.Web/Components/Pages/Dialogs/JobDetailsDialog.razor`
