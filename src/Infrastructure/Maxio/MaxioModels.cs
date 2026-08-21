using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
}

internal sealed class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal sealed class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}

internal sealed class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public string? Reference { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

internal sealed class MaxioCreateSubscription
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }

    /// <summary>
    /// Official collection method when no payment profile is supplied.
    /// Relationship Invoicing values: remittance, automatic, prepaid.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

public sealed class MaxioCustomerInfo
{
    public MaxioCustomerInfo(int id, string? reference)
    {
        Id = id;
        Reference = reference;
    }

    public int Id { get; }
    public string? Reference { get; }
}

public sealed class MaxioSubscriptionInfo
{
    public MaxioSubscriptionInfo(
        int id,
        string state,
        string productHandle,
        string productName,
        long priceInCents,
        DateTimeOffset? nextBillingAt)
    {
        Id = id;
        State = state;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        NextBillingAt = nextBillingAt;
    }

    public int Id { get; }
    public string State { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public long PriceInCents { get; }
    public DateTimeOffset? NextBillingAt { get; }
}

public sealed class MaxioProductInfo
{
    public MaxioProductInfo(
        string handle,
        string name,
        string? description,
        long priceInCents,
        int interval,
        string intervalUnit,
        string? productFamilyHandle)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        ProductFamilyHandle = productFamilyHandle;
    }

    public string Handle { get; }
    public string Name { get; }
    public string? Description { get; }
    public long PriceInCents { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
    public string? ProductFamilyHandle { get; }
}
