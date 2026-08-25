using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Dto;

// Wire DTOs matching the Maxio OpenAPI spec (maxio-spec/openapi.yaml).
// Property names are PascalCase here; (de)serialization uses the snake_case
// naming policy so they map to the spec's JSON field names.

// Customer-Response.yaml / Customer.yaml
internal class CustomerResponseDto
{
    public CustomerDto? Customer { get; set; }
}

internal class CustomerDto
{
    public long Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

// Create-Customer-Request.yaml / Create-Customer.yaml
internal class CreateCustomerRequestDto
{
    public CreateCustomerDto Customer { get; set; } = new();
}

internal class CreateCustomerDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

// Product-Response.yaml / Product.yaml
internal class ProductResponseDto
{
    public ProductDto? Product { get; set; }
}

internal class ProductDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; set; }
}

// Subscription-Response.yaml / Subscription.yaml
internal class SubscriptionResponseDto
{
    public SubscriptionDto? Subscription { get; set; }
}

internal class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public CustomerDto? Customer { get; set; }
    public ProductDto? Product { get; set; }
}

// Create-Subscription-Request.yaml / Create-Subscription.yaml
internal class CreateSubscriptionRequestDto
{
    public CreateSubscriptionDto Subscription { get; set; } = new();
}

internal class CreateSubscriptionDto
{
    public string ProductHandle { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;

    // eShopOnWeb never captures payment methods, so subscriptions are billed by
    // invoice (Collection-Method.yaml: "remittance") instead of automatic card charges.
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

// errors/Error-List-Response.yaml — errors is a list of messages. Some
// endpoints (e.g. Create Customer) return an object keyed by field instead;
// MaxioBillingClient normalizes both shapes.
internal class ErrorListResponseDto
{
    public List<string>? Errors { get; set; }
}
