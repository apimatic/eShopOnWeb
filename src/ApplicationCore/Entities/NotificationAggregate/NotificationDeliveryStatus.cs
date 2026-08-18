using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Vocabulary for a notification's delivery outcome. Provider-reported statuses (queued, sent,
/// delivered, undelivered, failed, canceled, scheduled, …) are stored verbatim; the few app-local
/// values below cover states the provider never assigns (a send that threw before a message id
/// existed, or a message id reserved for an in-flight resend).
/// </summary>
public static class NotificationDeliveryStatus
{
    /// <summary>A resend record whose provider send has not completed yet (reserves the idempotency key).</summary>
    public const string Pending = "pending";

    /// <summary>The provider call threw before a message id was returned — nothing left the app.</summary>
    public const string SendFailed = "send_failed";

    /// <summary>A follow-up accepted by the provider for future delivery.</summary>
    public const string Scheduled = "scheduled";

    /// <summary>A scheduled follow-up called off before it went out.</summary>
    public const string Canceled = "canceled";

    /// <summary>
    /// True when <paramref name="status"/> is a settled outcome that will not change, so there is
    /// no value in re-fetching it from the provider.
    /// </summary>
    public static bool IsTerminal(string? status) => status switch
    {
        "delivered" or "undelivered" or "failed" or "canceled" or "received"
            or "read" or "partially_delivered" or SendFailed => true,
        _ => false
    };
}
