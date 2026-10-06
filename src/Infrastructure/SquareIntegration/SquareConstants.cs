using System;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

public static class SquareConstants
{
    /// <summary>Permissions the merchant is asked to approve, space separated in one value.</summary>
    public const string OAuthScopes = "MERCHANT_PROFILE_READ ITEMS_READ ITEMS_WRITE ORDERS_READ ORDERS_WRITE";

    /// <summary>How long a started sign-in may take before its state is no longer accepted.</summary>
    public static readonly TimeSpan OAuthStateLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Refresh a connected merchant's access token when it expires within this window.</summary>
    public static readonly TimeSpan AccessTokenRefreshWindow = TimeSpan.FromDays(1);

    /// <summary>Upper bound on how long the SDK may cache a token before asking the token source again.</summary>
    public static readonly TimeSpan TokenCacheLifetime = TimeSpan.FromMinutes(5);

    /// <summary>SKU on the Square variation that marks an item as created by this shop for an eShop catalog item.</summary>
    public static string SkuFor(int catalogItemId) => $"eshop-{catalogItemId}";

    /// <summary>Key of the order custom attribute (Square "custom field") that holds the gift message.</summary>
    public const string GiftMessageAttributeKey = "eshop-gift-message";
    public const string GiftMessageAttributeName = "Gift message";
    public const string GiftMessageAttributeDescription = "Gift message the customer entered with their online order.";

    /// <summary>JSON schema Square uses for a custom field that holds text.</summary>
    public const string TextAttributeSchemaJson =
        "{\"$ref\":\"https://developer-production-s.squarecdn.com/schemas/v1/common.json#squareup.common.String\"}";

    public const int MaxGiftMessageLength = 200;
    public const int MaxPhotoBytes = 5 * 1024 * 1024;

    /// <summary>Per-request budget for a Square-backed API call (bounds all SDK calls the handler makes).</summary>
    public static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan CatalogSyncBudget = TimeSpan.FromSeconds(120);
}
