using System.Collections.Concurrent;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface INotificationRedactionState
{
    void MarkRedacted(int notificationId);

    bool IsRedacted(int notificationId);
}

public sealed class NotificationRedactionState : INotificationRedactionState
{
    private readonly ConcurrentDictionary<int, byte> _redacted = new();

    public void MarkRedacted(int notificationId) => _redacted[notificationId] = 1;

    public bool IsRedacted(int notificationId) => _redacted.ContainsKey(notificationId);
}
