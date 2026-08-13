namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Body for a resend. The caller-supplied idempotency key makes a repeat of the same request safe:
/// repeating under the same key does not send a second message.
/// </summary>
public class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}
