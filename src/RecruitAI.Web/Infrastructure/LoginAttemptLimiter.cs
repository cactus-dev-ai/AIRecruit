using System.Collections.Concurrent;

namespace RecruitAI.Web.Infrastructure;

public sealed class LoginAttemptLimiter
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _attempts = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAcquire(string ip, string userName)
    {
        var key = $"{ip}|{userName.Trim().ToUpperInvariant()}";
        var queue = _attempts.GetOrAdd(key, _ => new Queue<DateTime>());
        lock (queue)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-1);
            while (queue.Count > 0 && queue.Peek() < cutoff) queue.Dequeue();
            if (queue.Count >= 10) return false;
            queue.Enqueue(DateTime.UtcNow); return true;
        }
    }
}
