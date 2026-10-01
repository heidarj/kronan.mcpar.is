using Kronan.McparIs.Options;
using Kronan.McparIs.Services;
using Microsoft.Extensions.Options;

namespace Kronan.McparIs.Infrastructure;

// Single-process controls. Preflight reads and all adapters spend the same budget.
public sealed class RequestBudget(TimeProvider time, IOptions<LimitsOptions> options)
{
    private readonly object gate = new();
    private readonly Queue<double> upstream = new();
    private readonly Dictionary<string, Queue<double>> tools = new(StringComparer.Ordinal);
    private readonly Queue<double> mutations = new();
    private readonly SemaphoreSlim concurrent = new(2, 2);
    private readonly double started = time.GetElapsedTime(0, time.GetTimestamp()).TotalSeconds;
    private double nextDispatch;
    private double upstreamCooldown;
    private double toolTokens = 10;
    private double toolRefill = time.GetElapsedTime(0, time.GetTimestamp()).TotalSeconds;
    private double mutationTokens = 5;
    private double mutationRefill = time.GetElapsedTime(0, time.GetTimestamp()).TotalSeconds;
    private double Now => time.GetElapsedTime(0, time.GetTimestamp()).TotalSeconds;
    public int StartupRetrySeconds => Math.Max(0, (int)Math.Ceiling(started + options.Value.StartupCooldownSeconds - Now));

    public void Tool(string subject)
    {
        lock (gate)
        {
            if (!tools.TryGetValue(subject, out var queue)) tools[subject] = queue = new();
            ConsumeWindow(queue, options.Value.ToolRequestsPerMinute, 60);
            ConsumeBurst(ref toolTokens, ref toolRefill, 10, options.Value.ToolRequestsPerMinute);
        }
    }

    public void Mutation()
    {
        lock (gate)
        {
            ConsumeWindow(mutations, options.Value.MutationRequestsPerMinute, 60);
            ConsumeBurst(ref mutationTokens, ref mutationRefill, 5, options.Value.MutationRequestsPerMinute);
        }
    }

    private void ConsumeWindow(Queue<double> queue, int capacity, int seconds)
    {
        var now = Now;
        while (queue.TryPeek(out var first) && first <= now - seconds) queue.Dequeue();
        if (queue.Count >= capacity)
            throw new ServiceFailure("rate_limited", "Please wait before trying again.", Math.Max(1, (int)Math.Ceiling(queue.Peek() + seconds - now)));
        queue.Enqueue(now);
    }

    private void ConsumeBurst(ref double tokens, ref double last, int burst, int perMinute)
    {
        var now = Now;
        tokens = Math.Min(burst, tokens + (now - last) * perMinute / 60);
        last = now;
        if (tokens < 1) throw new ServiceFailure("rate_limited", "Please wait before trying again.", Math.Max(1, (int)Math.Ceiling((1 - tokens) * 60 / perMinute)));
        tokens--;
    }

    public async Task<IDisposable> EnterUpstreamAsync(CancellationToken cancellationToken)
    {
        if (!await concurrent.WaitAsync(0, cancellationToken))
            throw new ServiceFailure("concurrent_limit", "The shopping connection is busy. Please try again shortly.", 2);
        try
        {
            while (true)
            {
                double wait;
                lock (gate)
                {
                    var now = Now;
                    var blockedUntil = Math.Max(started + options.Value.StartupCooldownSeconds, upstreamCooldown);
                    if (now < blockedUntil)
                        throw new ServiceFailure("upstream_cooldown", "The shopping connection is temporarily cooling down.", Math.Max(1, (int)Math.Ceiling(blockedUntil - now)));
                    while (upstream.TryPeek(out var first) && first <= now - 200) upstream.Dequeue();
                    if (upstream.Count >= options.Value.UpstreamRequests)
                        throw new ServiceFailure("rate_limited", "The Krónan request budget is temporarily exhausted.", Math.Max(1, (int)Math.Ceiling(upstream.Peek() + 200 - now)));
                    wait = nextDispatch - now;
                    if (wait <= 0)
                    {
                        upstream.Enqueue(now);
                        nextDispatch = now + options.Value.PacingMilliseconds / 1000d;
                        return new Permit(concurrent);
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(wait), time, cancellationToken);
            }
        }
        catch { concurrent.Release(); throw; }
    }

    public void Cooldown(TimeSpan duration)
    {
        lock (gate) upstreamCooldown = Math.Max(upstreamCooldown, Now + Math.Clamp(duration.TotalSeconds, 1, 86400));
    }

    private sealed class Permit(SemaphoreSlim semaphore) : IDisposable
    {
        private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) semaphore.Release(); }
    }
}
