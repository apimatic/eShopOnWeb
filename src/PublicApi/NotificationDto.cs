using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// The view of a single notification returned by the API. It carries the notification's own identifier (what the
/// operator endpoints act on) and the provider-owned state (identifier and current delivery outcome). The
/// shopper's number is never exposed in full — only a masked form — and the message text is omitted once its
/// content has been disposed of.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }

    /// <summary>Why the message was sent (order placed, dispatched, delivery follow-up, cancelled, resend).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Where the message got to: the provider's status when known, otherwise a local marker.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The provider's own identifier for the message, if it was accepted by the provider.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>The provider's current delivery outcome for the message.</summary>
    public string? ProviderStatus { get; set; }

    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }

    /// <summary>A PII-free note when the message could not be handed to the provider at all.</summary>
    public string? LocalError { get; set; }

    /// <summary>For scheduled follow-ups, when the provider was asked to send the message.</summary>
    public DateTimeOffset? ScheduledSendAt { get; set; }

    /// <summary>True once the message content has been disposed of.</summary>
    public bool ContentRedacted { get; set; }

    /// <summary>The destination number, masked to its last four digits.</summary>
    public string? MaskedTo { get; set; }

    /// <summary>The message text; null once the content has been disposed of.</summary>
    public string? Body { get; set; }

    public DateTimeOffset CreatedDate { get; set; }

    public static NotificationDto FromEntity(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.EffectiveStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderStatus = n.ProviderStatus,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        LocalError = n.LocalError,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentRedacted = n.ContentRedacted,
        MaskedTo = Mask(n.ToNumber),
        Body = n.Body,
        CreatedDate = n.CreatedDate
    };

    private static string? Mask(string? number)
    {
        if (string.IsNullOrEmpty(number))
            return number;
        if (number.Length <= 4)
            return new string('*', number.Length);
        return string.Concat(new string('*', number.Length - 4), number.Substring(number.Length - 4));
    }
}
