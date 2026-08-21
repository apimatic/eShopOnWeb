using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

// JSON shapes verified against Maxio Advanced Billing API examples
// (maxio-com/ab-dotnet-sdk 9.1.0 models: Customer, Product, Subscription,
// CreateCustomer, CreateSubscription).

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomerDto? Customer { get; set; }
}

internal sealed class MaxioCustomerDto
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }

    public BillingCustomer ToDomain() => new()
    {
        Id = Id,
        FirstName = FirstName ?? string.Empty,
        LastName = LastName ?? string.Empty,
        Email = Email ?? string.Empty,
        Reference = Reference ?? string.Empty
    };
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

internal sealed class MaxioProductEnvelope
{
    public MaxioProductDto? Product { get; set; }
}

internal sealed class MaxioProductDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamilyDto? ProductFamily { get; set; }

    public BillingProduct ToDomain() => new()
    {
        Id = Id,
        Name = Name ?? string.Empty,
        Handle = Handle ?? string.Empty,
        Description = Description ?? string.Empty,
        PriceInCents = PriceInCents,
        Interval = Interval,
        IntervalUnit = IntervalUnit ?? string.Empty,
        RequireCreditCard = RequireCreditCard,
        ArchivedAt = ArchivedAt,
        ProductFamilyHandle = ProductFamily?.Handle ?? string.Empty
    };
}

internal sealed class MaxioProductFamilyDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscriptionDto? Subscription { get; set; }
}

internal sealed class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? Reference { get; set; }
    public MaxioProductDto? Product { get; set; }

    public BillingSubscription ToDomain() => new()
    {
        Id = Id,
        State = State ?? string.Empty,
        ProductHandle = Product?.Handle ?? string.Empty,
        ProductName = Product?.Name ?? string.Empty,
        ProductPriceInCents = ProductPriceInCents != 0 ? ProductPriceInCents : Product?.PriceInCents ?? 0,
        NextBillingAt = NextAssessmentAt,
        CurrentPeriodEndsAt = CurrentPeriodEndsAt,
        CreatedAt = CreatedAt,
        Reference = Reference
    };
}

internal sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

internal sealed class MaxioCreateSubscription
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? Reference { get; set; }
    /// <summary>
    /// Invoice the customer instead of charging a card. Required for no-card
    /// signups on products that still have a recurring price. Official create
    /// example: CollectionMethod.Remittance (ab-dotnet-sdk 9.1.0).
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
