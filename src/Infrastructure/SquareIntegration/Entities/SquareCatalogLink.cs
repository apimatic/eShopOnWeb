using System;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration.Entities;

/// <summary>
/// Links an eShop catalog item to the Square item this shop created for it in one merchant's catalog.
/// The row is also the claim that keeps two syncs (or two photo uploads) from writing the same item twice.
/// </summary>
public class SquareCatalogLink
{
    public string MerchantId { get; set; } = string.Empty;
    public int CatalogItemId { get; set; }

    public string? SquareItemId { get; set; }
    public string? SquareVariationId { get; set; }
    public string State { get; set; } = SquareCatalogLinkState.PendingCreate;

    /// <summary>Idempotency key of the catalog write in flight (or whose outcome is still unknown).</summary>
    public string? PendingIdempotencyKey { get; set; }
    public string? PendingName { get; set; }
    public long? PendingAmount { get; set; }
    public string? PendingCurrency { get; set; }
    public DateTimeOffset? PendingSince { get; set; }

    public string? PhotoState { get; set; }
    public string? PhotoSha256 { get; set; }
    public string? PhotoIdempotencyKey { get; set; }
    public DateTimeOffset? PhotoClaimedAt { get; set; }
    public string? PhotoImageId { get; set; }
    public string? PhotoImageUrl { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public static class SquareCatalogLinkState
{
    public const string PendingCreate = "PendingCreate";
    public const string PendingUpdate = "PendingUpdate";
    public const string Synced = "Synced";
}

public static class SquarePhotoState
{
    public const string Pending = "Pending";
    public const string Done = "Done";
}
