namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Stable Maxio <c>reference</c> values that map an eShopOnWeb shopper to billing records.
/// Username (JWT name) is used because it survives in-memory identity reseeds; numeric user ids do not.
/// </summary>
public static class BillingReference
{
    public const string Prefix = "eshop";

    public static string ForCustomer(string shopperIdentity)
    {
        return $"{Prefix}:{RequireIdentity(shopperIdentity)}";
    }

    public static string ForSubscription(string shopperIdentity, string productHandle)
    {
        return $"{Prefix}:{RequireIdentity(shopperIdentity)}:{RequireHandle(productHandle)}";
    }

    public static decimal CentsToAmount(long cents) => cents / 100m;

    private static string RequireIdentity(string shopperIdentity)
    {
        if (string.IsNullOrWhiteSpace(shopperIdentity))
        {
            throw new System.ArgumentException("Shopper identity is required.", nameof(shopperIdentity));
        }

        return shopperIdentity.Trim();
    }

    private static string RequireHandle(string productHandle)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new System.ArgumentException("Product handle is required.", nameof(productHandle));
        }

        return productHandle.Trim();
    }
}
