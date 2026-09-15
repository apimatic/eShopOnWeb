using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.MaxioModels;

public sealed class ProductEnvelope
{
    public ProductDto? Product { get; set; }
}

public sealed class ProductDto
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class CustomerEnvelope
{
    public CustomerDto? Customer { get; set; }
}

public sealed class CustomerDto
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public sealed class CreateCustomerEnvelope
{
    public CreateCustomerBody Customer { get; set; } = new();
}

public sealed class CreateCustomerBody
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public sealed class SubscriptionEnvelope
{
    public SubscriptionDto? Subscription { get; set; }
}

public sealed class SubscriptionDto
{
    public long Id { get; set; }
    public string? State { get; set; }
    public int ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public string? Reference { get; set; }
    public ProductDto? Product { get; set; }
}

public sealed class CreateSubscriptionEnvelope
{
    public CreateSubscriptionBody Subscription { get; set; } = new();
}

public sealed class CreateSubscriptionBody
{
    public string ProductHandle { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// Remittance collection does not require a stored payment method (Maxio CollectionMethod).
    /// Sandbox plans are configured with payment method not required.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
