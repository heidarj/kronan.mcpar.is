using Kronan.McparIs.Authentication;
using Kronan.McparIs.Infrastructure;
using Kronan.McparIs.Models;
using Kronan.McparIs.Services;
using Microsoft.Extensions.Options;

namespace Kronan.McparIs.Tests;

public sealed class MutationTests
{
    private static MutationCoordinator Create(TestClock clock, int capacity = 512, int lifetime = 1200)
    {
        var limits = TestSupport.FastLimits(); limits.OperationCapacity = capacity; limits.OperationLifetimeSeconds = lifetime;
        return new(clock, Microsoft.Extensions.Options.Options.Create(limits), new RequestBudget(clock, Microsoft.Extensions.Options.Options.Create(limits)));
    }

    [Fact]
    public async Task Concurrent_replay_sends_one_mutation_and_returns_previous_result()
    {
        var ledger = Create(new TestClock()); var actor = new HouseholdActor("one");
        var prepared = ledger.Prepare(actor, TestSupport.Addition); var calls = 0;
        async Task<ShoppingNote> Send(ShoppingChange _, CancellationToken ct) { Interlocked.Increment(ref calls); await Task.Delay(30, ct); return TestSupport.Note; }
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ledger.ExecuteAsync(actor, prepared.OperationId, TestSupport.Addition, Send, default)));
        Assert.Equal(1, calls); Assert.All(results, r => Assert.Same(TestSupport.Note, r));
    }

    [Fact]
    public async Task Arguments_and_actor_are_bound_to_prepared_id()
    {
        var ledger = Create(new TestClock()); var actor = new HouseholdActor("one");
        var prepared = ledger.Prepare(actor, TestSupport.Addition);
        Task<ShoppingNote> Send(ShoppingChange _, CancellationToken ct) => throw new Exception("Must not dispatch");
        var conflict = await Assert.ThrowsAsync<ServiceFailure>(() => ledger.ExecuteAsync(actor, prepared.OperationId, new("add", [new("Milk")]), Send, default));
        Assert.Equal("operation_conflict", conflict.Code);
        var other = await Assert.ThrowsAsync<ServiceFailure>(() => ledger.ExecuteAsync(new("two"), prepared.OperationId, TestSupport.Addition, Send, default));
        Assert.Equal("forbidden", other.Code);
    }

    [Fact]
    public async Task Unknown_write_is_not_replayed()
    {
        var ledger = Create(new TestClock()); var actor = new HouseholdActor("one"); var calls = 0;
        var prepared = ledger.Prepare(actor, TestSupport.Addition);
        Task<ShoppingNote> Send(ShoppingChange _, CancellationToken ct) { calls++; throw new ServiceFailure("outcome_unknown", "Check the list.", outcomeUnknown: true); }
        for (var i = 0; i < 2; i++) Assert.True((await Assert.ThrowsAsync<ServiceFailure>(() => ledger.ExecuteAsync(actor, prepared.OperationId, TestSupport.Addition, Send, default))).OutcomeUnknown);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Expired_and_previous_generation_ids_never_become_new_operations()
    {
        var clock = new TestClock(); var actor = new HouseholdActor("one"); var first = Create(clock, lifetime: 60);
        var prepared = first.Prepare(actor, TestSupport.Addition);
        Task<ShoppingNote> Send(ShoppingChange _, CancellationToken ct) => throw new Exception("Must not dispatch");
        Assert.Equal("operation_expired", (await Assert.ThrowsAsync<ServiceFailure>(() => Create(clock).ExecuteAsync(actor, prepared.OperationId, TestSupport.Addition, Send, default))).Code);
        clock.Advance(60);
        Assert.Equal("operation_expired", (await Assert.ThrowsAsync<ServiceFailure>(() => first.ExecuteAsync(actor, prepared.OperationId, TestSupport.Addition, Send, default))).Code);
    }

    [Fact]
    public void Capacity_refuses_new_ids_instead_of_forgetting_live_replays()
    {
        var clock = new TestClock(); var ledger = Create(clock, capacity: 1, lifetime: 60); var actor = new HouseholdActor("one");
        ledger.Prepare(actor, TestSupport.Addition);
        Assert.Equal("operation_capacity", Assert.Throws<ServiceFailure>(() => ledger.Prepare(actor, TestSupport.Addition)).Code);
        clock.Advance(60); ledger.Prepare(actor, TestSupport.Addition);
    }

    [Theory]
    [InlineData("{\"action\":\"add\",\"items\":[]}")]
    [InlineData("{\"action\":\"add\",\"items\":[{\"text\":\"Butter\",\"sku\":\"123\"}]}")]
    [InlineData("{\"action\":\"add\",\"items\":[{\"text\":\" \"}]}")]
    [InlineData("{\"action\":\"add\",\"items\":[{\"text\":\"Butter\",\"quantity\":10001}]}")]
    [InlineData("{\"action\":\"update\",\"lineToken\":\"a1000000-0000-4000-8000-000000000001\"}")]
    [InlineData("{\"action\":\"remove\",\"lineToken\":\"00000000-0000-0000-0000-000000000000\"}")]
    public void Invalid_inputs_are_rejected_before_network_calls(string json)
    {
        var change = System.Text.Json.JsonSerializer.Deserialize<ShoppingChange>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal("invalid_input", Assert.Throws<ServiceFailure>(() => ShoppingValidation.Normalize(change)).Code);
    }
}
