# Logs Feature Fixes - Plan

**Date:** January 17, 2026  
**Page:** https://localhost:7268/temporary

## Issues to Fix

### Issue #1: Logs button on main page does not work
**Location:** [TEMPORARY_HomePage.razor](../src/BlazorOrchestrator.Web/Components/Pages/TEMPORARY/TEMPORARY_HomePage.razor)

**Problem:** The "Logs" button on each job card does not have a click handler - it's just a static button.

**Solution:** Add a click handler that opens a dialog showing logs related to the JobId in reverse order (most recent first).

**Changes Required:**
1. Add `Click` handler to all three Logs buttons (for running, enabled, and disabled job states)
2. Create a method `OpenLogsDialog(int jobId)` to show the logs dialog
3. Display logs in reverse chronological order (most recent first)

---

### Issue #2: Level column width is not wide enough on Logs tab
**Location:** [JobDetailsDialog.razor](../src/BlazorOrchestrator.Web/Components/Pages/Dialogs/JobDetailsDialog.razor#L156)

**Problem:** The Level column in the Logs DataGrid is set to `Width="80px"` which is too narrow for displaying the badge.

**Solution:** Increase the width to `Width="100px"` to properly display the level badge.

---

### Issue #3: Logs should be shown in reverse order
**Location:** [JobDetailsDialog.razor](../src/BlazorOrchestrator.Web/Components/Pages/Dialogs/JobDetailsDialog.razor#L281)

**Problem:** In the `LoadLogs()` method, logs are displayed in the order returned from the database, not in reverse chronological order.

**Solution:** Sort the logs by `CreatedDate` in descending order so the most recent logs appear first.

---

### Issue #4: Include Azure Table Storage logs (JobLogs table)
**Location:** 
- [JobManager.cs](../src/BlazorDataOrchestrator.Core/JobManager.cs)
- [JobDetailsDialog.razor](../src/BlazorOrchestrator.Web/Components/Pages/Dialogs/JobDetailsDialog.razor)
- [TEMPORARY_HomePage.razor](../src/BlazorOrchestrator.Web/Components/Pages/TEMPORARY/TEMPORARY_HomePage.razor)

**Problem:** Logs from Azure Table Storage (JobLogs table) were not being displayed. The partition key format is `{JobId}-{JobInstanceId}`.

**Solution:** 
1. Added `GetAllLogsForJobAsync(int jobId)` method to `JobManager.cs` to fetch all logs for a job from Azure Table Storage
2. Updated `LoadLogs()` in `JobDetailsDialog.razor` to merge Azure Table logs with job instance logs
3. Updated `OpenLogsDialog()` in `TEMPORARY_HomePage.razor` to include Azure Table logs
4. All logs are sorted by timestamp in descending order (most recent first)

---

## Implementation Status

- [x] Issue #1: Add Logs button click handler on main page
- [x] Issue #2: Fix Level column width in Logs tab
- [x] Issue #3: Sort logs in reverse order
- [x] Issue #4: Include Azure Table Storage logs (JobLogs table)
