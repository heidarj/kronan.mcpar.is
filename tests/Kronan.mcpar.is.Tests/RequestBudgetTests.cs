using Kronan.McparIs.Infrastructure;
using Kronan.McparIs.Options;
using Kronan.McparIs.Services;
using Microsoft.Extensions.Options;

namespace Kronan.McparIs.Tests;

public sealed class RequestBudgetTests
{
    [Fact]
    public async Task Restart_blocks_unknown_previous_budget_for_full_window()
    {
        var clock = new TestClock();
        var budget = new RequestBudget(clock, Microsoft.Extensions.Options.Options.Create(new LimitsOptions()));
        var error = await Assert.ThrowsAsync<ServiceFailure>(() => budget.EnterUpstreamAsync(default));
        Assert.Equal(200, error.RetryAfterSeconds);
        clock.Advance(199.9);
        await Assert.ThrowsAsync<ServiceFailure>(() => budget.EnterUpstreamAsync(default));
        clock.Advance(.1);
        using var permit = await budget.EnterUpstreamAsync(default);
    }

    [Fact]
    public async Task All_attempts_spend_rolling_capacity_and_it_expires()
    {
        var clock = new TestClock();
        var options = TestSupport.FastLimits(); options.UpstreamRequests = 3;
        var budget = new RequestBudget(clock, Microsoft.Extensions.Options.Options.Create(options));
        for (var i = 0; i < 3; i++) { using var permit = await budget.EnterUpstreamAsync(default); }
        var rejected = await Assert.ThrowsAsync<ServiceFailure>(() => budget.EnterUpstreamAsync(default));
        Assert.Equal("rate_limited", rejected.Code);
        clock.Advance(199);
        await Assert.ThrowsAsync<ServiceFailure>(() => budget.EnterUpstreamAsync(default));
        clock.Advance(1);
        using var next = await budget.EnterUpstreamAsync(default);
    }

    [Fact]
    public async Task Concurrent_calls_have_two_permits_and_release_on_disposal()
    {
        var budget = new RequestBudget(new TestClock(), Microsoft.Extensions.Options.Options.Create(TestSupport.FastLimits()));
        using var first = await budget.EnterUpstreamAsync(default);
        using var second = await budget.EnterUpstreamAsync(default);
        Assert.Equal("concurrent_limit", (await Assert.ThrowsAsync<ServiceFailure>(() => budget.EnterUpstreamAsync(default))).Code);
        first.Dispose(); first.Dispose();
        using var third = await budget.EnterUpstreamAsync(default);
    }

    [Fact]
    public async Task Shared_cooldown_survives_calls_from_other_adapters()
    {
        var clock = new TestClock();
        var budget = new RequestBudget(clock, Microsoft.Extensions.Options.Options.Create(TestSupport.FastLimits()));
        budget.Cooldown(TimeSpan.FromSeconds(30));
        budget.Cooldown(TimeSpan.FromSeconds(2));
        Assert.Equal(30, (await Assert.ThrowsAsync<ServiceFailure>(() => budget.EnterUpstreamAsync(default))).RetryAfterSeconds);
        clock.Advance(30);
        using var permit = await budget.EnterUpstreamAsync(default);
    }

    [Fact]
    public async Task Pacing_can_be_cancelled_without_leaking_a_concurrency_permit()
    {
        var limits = TestSupport.FastLimits(); limits.PacingMilliseconds = 2000;
        var clock = new TestClock();
        var budget = new RequestBudget(clock, Microsoft.Extensions.Options.Options.Create(limits));
        using (await budget.EnterUpstreamAsync(default)) { }
        using var cancel = new CancellationTokenSource();
        var waiting = budget.EnterUpstreamAsync(cancel.Token);
        Assert.False(waiting.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        clock.Advance(2);
        using var permit = await budget.EnterUpstreamAsync(default);
    }

    [Fact]
    public void Household_mutation_burst_is_shared()
    {
        var clock = new TestClock();
        var budget = new RequestBudget(clock, Microsoft.Extensions.Options.Options.Create(TestSupport.FastLimits()));
        for (var i = 0; i < 5; i++) budget.Mutation();
        Assert.Equal("rate_limited", Assert.Throws<ServiceFailure>(() => budget.Mutation()).Code);
        clock.Advance(3);
        budget.Mutation();
    }
}
