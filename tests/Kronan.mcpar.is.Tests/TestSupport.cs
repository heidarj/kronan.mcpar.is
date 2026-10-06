using System.Net;
using System.Text;
using Kronan.McparIs.Infrastructure;
using Kronan.McparIs.KronanApi;
using Kronan.McparIs.Models;
using Kronan.McparIs.Options;
using Microsoft.Extensions.Options;

namespace Kronan.McparIs.Tests;

internal sealed class TestClock : TimeProvider
{
    private long ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref ticks);
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-01T12:00:00Z").AddTicks(GetTimestamp());
    public void Advance(double seconds) => Interlocked.Add(ref ticks, (long)(seconds * TimeSpan.TicksPerSecond));
}

internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return send(request, cancellationToken);
    }
    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

internal static class TestSupport
{
    public const string NoteJson = "{\"token\":\"0b317fff-e427-4214-a583-a6ecda82f523\",\"name\":\"Shopping\",\"lines\":[{\"token\":\"a1000000-0000-4000-8000-000000000001\",\"text\":\"Butter\",\"quantity\":1,\"placement\":0,\"isCompleted\":false}]}";
    public static readonly ShoppingNote Note = new(Guid.NewGuid(), "Shopping", []);
    public static readonly ShoppingChange Addition = new("add", [new("Butter")]);
    public static LimitsOptions FastLimits() => new() { StartupCooldownSeconds = 0, PacingMilliseconds = 0 };
    public static KronanClient Client(StubHandler handler, TestClock? clock = null, RequestBudget? budget = null)
    {
        clock ??= new TestClock();
        budget ??= new RequestBudget(clock, Microsoft.Extensions.Options.Options.Create(FastLimits()));
        return new KronanClient(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(new KronanOptions { ApiKey = "synthetic-test-token" }), budget, clock);
    }
}
