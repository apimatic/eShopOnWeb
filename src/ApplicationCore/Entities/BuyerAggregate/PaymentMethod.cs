using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card the buyer saved for later orders. Only the processor's token and the details needed to
/// recognise the card are kept; the card data itself lives with the processor (PayPal vault).
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string cardId, string? providerCustomerId, string? brand, string? last4, string? expiry, string? alias, string? saveRequestKey, DateTimeOffset createdAt)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(cardId, nameof(cardId));

        BuyerId = buyerId;
        CardId = cardId;
        ProviderCustomerId = providerCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
        SaveRequestKey = saveRequestKey;
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; }
    public string? Alias { get; private set; }
    public string? CardId { get; private set; } // actual card data must be stored in a PCI compliant system, like Stripe
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    /// <summary>ISO-8601 year-month, <c>YYYY-MM</c>.</summary>
    public string? Expiry { get; private set; }
    public string? ProviderCustomerId { get; private set; }
    /// <summary>Caller-supplied idempotency key the card was saved under, if any.</summary>
    public string? SaveRequestKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    /// <summary>Removed here, but the processor has not yet confirmed deleting its token.</summary>
    public bool ProviderDeletionPending { get; private set; }

    public bool IsRemoved => RemovedAt is not null;

    public void Remove(DateTimeOffset now)
    {
        RemovedAt ??= now;
        ProviderDeletionPending = true;
    }

    public void ConfirmProviderDeletion() => ProviderDeletionPending = false;
}
