using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration.Data;

/// <summary>
/// The Square merchant this shop acts for after the merchant signed in through OAuth.
/// There is at most one row (<see cref="SingletonId"/>). Tokens are stored encrypted.
/// </summary>
public class SquareMerchantConnection
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public string MerchantId { get; set; } = string.Empty;
    public string ProtectedAccessToken { get; set; } = string.Empty;
    public string? ProtectedRefreshToken { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A sign-in the shop started. The callback must present one of these, once, before it expires.
/// </summary>
public class SquareOAuthState
{
    public string State { get; set; } = string.Empty;
    public string? StartedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Which Square item/variation represents an eShop catalog item for a given merchant.
/// </summary>
public class SquareCatalogLink
{
    public string MerchantId { get; set; } = string.Empty;
    public int CatalogItemId { get; set; }
    public string SquareItemId { get; set; } = string.Empty;
    public string SquareVariationId { get; set; } = string.Empty;
    public string? SquareImageId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum SquareOrderSyncStatus
{
    /// <summary>The eShop order is saved; the Square order has not been confirmed yet.</summary>
    PendingSquareOrder = 0,
    /// <summary>The Square order exists; the gift message has not been confirmed yet.</summary>
    PendingGiftMessage = 1,
    /// <summary>The Square order (and gift message, if any) are confirmed.</summary>
    Synced = 2,
}

/// <summary>
/// Ties an eShop <see cref="Order"/> to the Square order created for it. Holds the
/// idempotency key of the Square write so a retry can never create a second Square order.
/// The gift message itself is never stored here — it lives on the Square order.
/// </summary>
public class SquareOrderLink
{
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public string MerchantId { get; set; } = string.Empty;
    public string LocationId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? SquareOrderId { get; set; }

    /// <summary>
    /// The Square variation each order item was sent with, as "catalogItemId=variationId" pairs (empty = ad-hoc line),
    /// so a re-send under the same idempotency key carries exactly the same lines.
    /// </summary>
    public string LineCatalogObjectIds { get; set; } = string.Empty;
    public bool HasGiftMessage { get; set; }
    public SquareOrderSyncStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A short-lived, store-enforced claim (primary key) that serializes an operator action
/// across requests and instances. Expired leases may be taken over.
/// </summary>
public class SquareLease
{
    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
