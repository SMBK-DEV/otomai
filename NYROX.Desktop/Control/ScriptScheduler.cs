using System.Collections.Concurrent;
using BubbleBot.Cli;

namespace NYROX.Desktop.Control;

/// <summary>
/// Explicit, per-account callback scheduler. It never auto-starts a profile.
/// Lua callbacks run synchronously on a worker thread; a callback must return
/// promptly because MoonSharp execution cannot be forcefully interrupted safely.
/// </summary>
internal sealed class ScriptScheduler : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, RunningSchedule> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly ControlLogStore _logs;
    private readonly ProfileStore _profiles;

    public ScriptScheduler(ControlLogStore logs, ProfileStore profiles)
    {
        _logs = logs;
        _profiles = profiles;
    }

    public IReadOnlyList<ScheduleStatus> Status() => _running.Values
        .Select(x => new ScheduleStatus(x.AccountId, x.ProfileName, x.CallbackName, x.IntervalMs, x.StartedAt))
        .OrderBy(x => x.AccountId, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool Start(string accountId, string profileName, out string? error)
    {
        error = null;
        if (!BotManager.Instance.GetGameClients().TryGetValue(accountId, out var client))
        {
            error = "Account is not present in the live runtime.";
            return false;
        }
        if (!client.Connected || client.IsStopped)
        {
            error = "Account must be connected and not stopped.";
            return false;
        }
        var profile = _profiles.Get(profileName);
        if (profile is null)
        {
            error = "Profile was not found or is invalid.";
            return false;
        }
        if (_running.ContainsKey(accountId))
        {
            error = "A scheduled callback is already running for this account.";
            return false;
        }

        // Validate callback and loaded script before creating the background loop.
        var probe = client.InvokeLuaCallback(profile.CallbackName);
        if (!probe.Succeeded)
        {
            error = probe.Error ?? "Callback validation failed. Load the profile's script first.";
            return false;
        }

        var cts = new CancellationTokenSource();
        var running = new RunningSchedule(accountId, profile.Name, profile.CallbackName, profile.IntervalMs,
            DateTimeOffset.UtcNow, cts);
        if (!_running.TryAdd(accountId, running))
        {
            cts.Dispose();
            error = "A scheduler was started concurrently for this account.";
            return false;
        }

        _logs.Write("Information", accountId, $"Scheduler started: profile={profile.Name}, callback={profile.CallbackName}, interval={profile.IntervalMs}ms.");
        running.Worker = Task.Run(() => RunAsync(client, running));
        return true;
    }

    public async Task<bool> StopAsync(string accountId)
    {
        if (!_running.TryRemove(accountId, out var running)) return false;
        running.Cancellation.Cancel();
        if (running.Worker is not null)
        {
            try { await running.Worker.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { _logs.Write("Warning", accountId, "Scheduler stop is waiting for a synchronous Lua callback to return."); }
            catch (OperationCanceledException) { }
        }
        running.Cancellation.Dispose();
        _logs.Write("Information", accountId, "Scheduler stopped.");
        return true;
    }

    private async Task RunAsync(BotGameClient client, RunningSchedule running)
    {
        try
        {
            while (!running.Cancellation.IsCancellationRequested)
            {
                await Task.Delay(running.IntervalMs, running.Cancellation.Token);
                if (!client.Connected || client.IsStopped)
                {
                    _logs.Write("Warning", running.AccountId, "Scheduler stopped because the game session is no longer active.");
                    break;
                }

                var result = await Task.Run(() => client.InvokeLuaCallback(running.CallbackName));
                if (!result.Succeeded)
                {
                    _logs.Write("Error", running.AccountId, $"Lua callback '{running.CallbackName}' failed: {result.Error}");
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _logs.Write("Error", running.AccountId, $"Scheduler failed: {exception.Message}");
        }
        finally
        {
            _running.TryRemove(new KeyValuePair<string, RunningSchedule>(running.AccountId, running));
            running.Cancellation.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var accountId in _running.Keys.ToArray())
            await StopAsync(accountId);
    }

    private sealed class RunningSchedule(
        string accountId, string profileName, string callbackName, int intervalMs,
        DateTimeOffset startedAt, CancellationTokenSource cancellation)
    {
        public string AccountId { get; } = accountId;
        public string ProfileName { get; } = profileName;
        public string CallbackName { get; } = callbackName;
        public int IntervalMs { get; } = intervalMs;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task? Worker { get; set; }
    }
}

internal sealed record ScheduleStatus(string AccountId, string ProfileName, string CallbackName, int IntervalMs, DateTimeOffset StartedAt);
