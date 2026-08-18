using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A Maxio product offered as an eShopOnWeb subscription plan.
/// </summary>
public class SubscriptionPlan
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public bool RequireCreditCard { get; init; }
    public string? ProductFamilyHandle { get; init; }
}

/// <summary>
/// A Maxio subscription owned by an eShopOnWeb shopper.
/// </summary>
public class ShopperSubscription
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string State { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public decimal Price => PriceInCents / 100m;
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}

public class BillingCustomer
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
}

public class CreateBillingCustomerRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
}

public class CreateBillingSubscriptionRequest
{
    public string ProductHandle { get; init; } = string.Empty;
    public int CustomerId { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string PaymentCollectionMethod { get; init; } = "remittance";
}

public class ShopperProfile
{
    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
}

public class SubscribeToPlanRequest
{
    public ShopperProfile Shopper { get; init; } = new();
    public string ProductHandle { get; init; } = string.Empty;
}

public class SubscribeToPlanResult
{
    public ShopperSubscription Subscription { get; init; } = new();
    public bool AlreadySubscribed { get; init; }
}

public static class MaxioReference
{
    public const string CustomerPrefix = "eshop-user:";
    public const string SubscriptionPrefix = "eshop-sub:";

    public static string ForCustomer(string userId) => $"{CustomerPrefix}{userId}";

    public static string ForSubscription(string userId, string productHandle) =>
        $"{SubscriptionPrefix}{userId}:{productHandle}";
}

public static class ShopperName
{
    public static (string FirstName, string LastName) FromProfile(ShopperProfile shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.UserName)
            ? shopper.UserName
            : shopper.Email;

        var local = source.Contains('@', StringComparison.Ordinal)
            ? source.Split('@')[0]
            : source;

        local = local.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Trim();
        var parts = local.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
        {
            return (Capitalize(parts[0]), Capitalize(string.Join(' ', parts[1..])));
        }

        if (parts.Length == 1)
        {
            return (Capitalize(parts[0]), "Shopper");
        }

        return ("eShop", "Shopper");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + (value.Length > 1 ? value[1..] : string.Empty);
    }
}
