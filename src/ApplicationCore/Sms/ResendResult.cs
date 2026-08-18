using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

public enum ResendStatus
{
    /// <summary>A message was (re)sent, or an earlier send under the same idempotency key was returned.</summary>
    Sent,

    /// <summary>No notification with the given id exists.</summary>
    SourceNotFound,

    /// <summary>The destination number has since been removed from the owner's file, so nothing may be sent to it again.</summary>
    NumberNoLongerOnFile
}

/// <summary>The outcome of an operator re-send request.</summary>
public class ResendResult
{
    private ResendResult(ResendStatus status, OrderNotification? notification)
    {
        Status = status;
        Notification = notification;
    }

    public ResendStatus Status { get; }

    /// <summary>The message the re-send produced (new, or the existing one for a repeated idempotency key).</summary>
    public OrderNotification? Notification { get; }

    public static ResendResult Sent(OrderNotification notification) => new(ResendStatus.Sent, notification);

    public static ResendResult SourceNotFound() => new(ResendStatus.SourceNotFound, null);

    public static ResendResult NumberNoLongerOnFile() => new(ResendStatus.NumberNoLongerOnFile, null);
}
