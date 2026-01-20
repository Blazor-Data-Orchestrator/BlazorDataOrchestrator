# Queue Message Visibility Timeout Renewal

## Problem Statement

When the `Worker.cs` processes a job that takes longer than the initial 5-minute visibility timeout, the message becomes visible again in the queue. This could cause:

- Duplicate job processing by another agent
- Wasted resources
- Potential data corruption or inconsistent state

## Solution Overview

Implement a **visibility timeout renewal pattern** that periodically extends the message's visibility timeout while the job is being processed. This is commonly called a "lease renewal" or "heartbeat" pattern.

## Implementation Details

### Key Parameters

| Parameter | Value | Description |
|-----------|-------|-------------|
| Initial visibility timeout | 5 minutes | How long the message is hidden when first received |
| Renewal interval | 3 minutes | How often to renew before the timeout expires |
| Renewal extension | 5 minutes | How much time to add with each renewal |

### Architecture Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                     Message Processing Flow                         │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  1. Receive message (5-min visibility timeout)                      │
│                     │                                               │
│                     ▼                                               │
│  2. Start visibility renewal task ────────────────────────┐         │
│                     │                                     │         │
│                     ▼                                     │         │
│  3. Process job ◄───────────────────────────────────────┐ │         │
│                     │                                   │ │         │
│                     │     ┌─────────────────────────┐   │ │         │
│                     │     │ Every 3 minutes:        │   │ │         │
│                     │     │ - Call UpdateMessageAsync│   │ │         │
│                     │     │ - Extend visibility 5min│   │ │         │
│                     │     │ - Update popReceipt     │───┘ │         │
│                     │     └─────────────────────────┘     │         │
│                     │                                     │         │
│                     ▼                                     │         │
│  4. Job completes ────────────────────────────────────────┘         │
│                     │                                               │
│                     ▼                                               │
│  5. Cancel renewal task                                             │
│                     │                                               │
│                     ▼                                               │
│  6. Delete message (with latest popReceipt)                         │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Code Changes

#### 1. Add Constants

```csharp
private static readonly TimeSpan VisibilityTimeout = TimeSpan.FromMinutes(5);
private static readonly TimeSpan RenewalInterval = TimeSpan.FromMinutes(3);
```

#### 2. Add Visibility Renewal Method

A new private method `RenewMessageVisibilityAsync` that:
- Takes the `QueueClient`, message ID, pop receipt, and a `CancellationToken`
- Runs in a loop on a timer (every 3 minutes)
- Calls `queueClient.UpdateMessageAsync()` to extend visibility
- Updates the pop receipt (it changes after each update)
- Logs renewal activity
- Stops when cancellation is requested

#### 3. Modify Message Processing Loop

- Create a `CancellationTokenSource` for the renewal task
- Start the visibility renewal task in parallel with job processing
- Cancel the renewal task after processing completes (success or failure)
- Use the latest pop receipt when deleting the message

### Error Handling

- If renewal fails, log a warning but continue processing
- Handle `OperationCanceledException` gracefully when the renewal task is cancelled
- If the message becomes visible before renewal, another agent might pick it up (idempotency is important)
- Track the latest pop receipt for message deletion

### Files Modified

| File | Changes |
|------|---------|
| `src/BlazorOrchestrator.Agent/Worker.cs` | Add constants, renewal method, modify processing loop |

### Testing Considerations

- Test with jobs that take longer than 5 minutes
- Verify message is not reprocessed by another agent
- Test cancellation scenarios
- Test network failures during renewal

### Future Enhancements

Consider making these configurable via `appsettings.json`:
- `MessageVisibilityTimeout`: Initial and renewal timeout
- `VisibilityRenewalInterval`: How often to renew
