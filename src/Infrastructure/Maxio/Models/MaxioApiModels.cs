using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

internal sealed class CustomerResponse
{
    public CustomerDto? Customer { get; set; }
}

internal sealed class CustomerDto
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class CreateCustomerRequest
{
    public CreateCustomerAttributes Customer { get; set; } = new();
}

internal sealed class CreateCustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class ProductResponse
{
    public ProductDto? Product { get; set; }
}

internal sealed class ProductDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class SubscriptionResponse
{
    public SubscriptionDto? Subscription { get; set; }
}

internal sealed class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ProductDto? Product { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionAttributes Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionAttributes
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? Reference { get; set; }
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

internal sealed class ErrorListResponse
{
    public List<string>? Errors { get; set; }
}
