using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

internal sealed class MaxioCustomerWrapper
{
    public MaxioCustomerDto? Customer { get; set; }
}

internal sealed class MaxioCustomerDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    public BillingCustomer ToBillingCustomer() => new()
    {
        Id = Id,
        Reference = Reference,
        Email = Email,
        FirstName = FirstName,
        LastName = LastName
    };
}

internal sealed class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomerPayload? Customer { get; set; }
}

internal sealed class MaxioCreateCustomerPayload
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class MaxioProductWrapper
{
    public MaxioProductDto? Product { get; set; }
}

internal sealed class MaxioProductDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamilyDto? ProductFamily { get; set; }

    public BillingProduct ToBillingProduct() => new()
    {
        Id = Id,
        Handle = Handle ?? string.Empty,
        Name = Name,
        Description = Description,
        PriceInCents = PriceInCents,
        Interval = Interval,
        IntervalUnit = IntervalUnit,
        ProductFamilyHandle = ProductFamily?.Handle,
        ArchivedAt = ArchivedAt
    };
}

internal sealed class MaxioProductFamilyDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

internal sealed class MaxioSubscriptionWrapper
{
    public MaxioSubscriptionDto? Subscription { get; set; }
}

internal sealed class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Reference { get; set; }
    public MaxioProductDto? Product { get; set; }
    public MaxioCustomerDto? Customer { get; set; }

    public BillingSubscription ToBillingSubscription() => new()
    {
        Id = Id,
        State = State,
        ProductPriceInCents = ProductPriceInCents,
        CurrentPeriodEndsAt = CurrentPeriodEndsAt,
        NextAssessmentAt = NextAssessmentAt,
        ActivatedAt = ActivatedAt,
        CreatedAt = CreatedAt,
        Reference = Reference,
        Product = Product?.ToBillingProduct(),
        Customer = Customer?.ToBillingCustomer()
    };
}

internal sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscriptionPayload? Subscription { get; set; }
    public string? UniquenessToken { get; set; }
}

internal sealed class MaxioCreateSubscriptionPayload
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? Reference { get; set; }
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
