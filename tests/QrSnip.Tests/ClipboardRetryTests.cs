using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using QrSnip.Clipboard;
using Xunit;

namespace QrSnip.Tests;

// Tests the retry policy in isolation from the real clipboard. The fake
// operation is just a counter; the fake delay is a no-op so tests don't
// actually wait the ~400ms budget.
public sealed class ClipboardRetryTests
{
    private static readonly Func<TimeSpan, Task> NoDelay = _ => Task.CompletedTask;

    [Fact]
    public async Task First_attempt_succeeds_returns_true_with_one_call()
    {
        var callCount = 0;
        var result = await ClipboardRetry.TryAsync(
            operation: () => { callCount++; return Task.FromResult(true); },
            delayProvider: NoDelay);

        Assert.True(result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task All_attempts_fail_returns_false_after_max_attempts()
    {
        var callCount = 0;
        var result = await ClipboardRetry.TryAsync(
            operation: () => { callCount++; return Task.FromResult(false); },
            delayProvider: NoDelay);

        Assert.False(result);
        Assert.Equal(ClipboardRetry.MaxAttempts, callCount);
    }

    [Fact]
    public async Task Succeeds_on_third_attempt_returns_true()
    {
        var callCount = 0;
        var result = await ClipboardRetry.TryAsync(
            operation: () =>
            {
                callCount++;
                return Task.FromResult(callCount >= 3);
            },
            delayProvider: NoDelay);

        Assert.True(result);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task Delay_called_between_attempts_but_not_after_last()
    {
        var delays = new List<TimeSpan>();
        await ClipboardRetry.TryAsync(
            operation: () => Task.FromResult(false),
            delayProvider: d => { delays.Add(d); return Task.CompletedTask; });

        // MaxAttempts attempts means (MaxAttempts - 1) delays in between.
        Assert.Equal(ClipboardRetry.MaxAttempts - 1, delays.Count);
        Assert.All(delays, d => Assert.Equal(ClipboardRetry.BackoffPerAttempt, d));
    }
}
