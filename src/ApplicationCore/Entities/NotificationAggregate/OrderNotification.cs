using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS raised for an order as it moves. The record carries enough of the state the provider
/// owns — its message identifier (<see cref="ProviderMessageSid"/>) and current delivery outcome
/// (<see cref="DeliveryStatus"/>) — that a later request can act on it (cancel a scheduled follow-up,
/// re-send, redact, reconcile) and report on it, not only the request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, string toNumber, int? contactNumberId,
        NotificationKind kind, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>Owner of the order/number this message concerns — used to scope shopper queries.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The canonical destination this message was addressed to. A shopper contact detail — never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The registered number this message targeted, or null once that number has been removed.</summary>
    public int? ContactNumberId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of at the shopper's request.</summary>
    public string? Body { get; private set; }

    /// <summary>True once the content has been redacted at the provider and cleared here.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>The provider's message identifier, or null if the message never reached the provider.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's (or our local) delivery outcome — see <see cref="SmsDeliveryStatus"/>.</summary>
    public string? DeliveryStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    /// <summary>Provider-supplied error text. Not a shopper contact detail; safe to surface to operators.</summary>
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>The caller-supplied idempotency key of the re-send that produced this record, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this record was re-sent from, if it is the product of a re-send.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Records the provider's acceptance of the message: its SID and the status it came back with.</summary>
    public void MarkSentToProvider(string providerMessageSid, string? status)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = status;
        SentAt = DateTimeOffset.UtcNow;
        ProviderErrorCode = null;
        ProviderErrorMessage = null;
    }

    /// <summary>Records that the message could not be handed to the provider at all.</summary>
    public void MarkSendFailed(int? errorCode, string? errorMessage)
    {
        DeliveryStatus = SmsDeliveryStatus.SendFailed;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Refreshes the delivery outcome from a later read of the provider's record.</summary>
    public void UpdateDeliveryStatus(string? status, int? errorCode, string? errorMessage)
    {
        DeliveryStatus = status;
        if (errorCode.HasValue) ProviderErrorCode = errorCode;
        if (!string.IsNullOrEmpty(errorMessage)) ProviderErrorMessage = errorMessage;
    }

    public void MarkScheduleCanceled() => DeliveryStatus = SmsDeliveryStatus.Canceled;

    public void SetResendMetadata(int resendOfNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>Clears the stored text after it has been redacted at the provider. The record and outcome survive.</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }
}
