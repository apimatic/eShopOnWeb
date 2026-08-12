namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Notifications;

/// <summary>Outcome of an operator resend.</summary>
public class ResendResult
{
    private ResendResult() { }

    /// <summary>False when the notification to resend does not exist.</summary>
    public bool Found { get; private init; }

    /// <summary>
    /// The id of the notification the resend produced. Under a repeated idempotency key this is the
    /// id of the message the first request already produced — no second message is sent.
    /// </summary>
    public int NotificationId { get; private init; }

    /// <summary>True when this call matched an existing idempotency key and sent nothing new.</summary>
    public bool WasIdempotentReplay { get; private init; }

    public static ResendResult NotFound() => new() { Found = false };

    public static ResendResult Sent(int notificationId) => new()
    {
        Found = true,
        NotificationId = notificationId,
        WasIdempotentReplay = false
    };

    public static ResendResult Replayed(int notificationId) => new()
    {
        Found = true,
        NotificationId = notificationId,
        WasIdempotentReplay = true
    };
}
