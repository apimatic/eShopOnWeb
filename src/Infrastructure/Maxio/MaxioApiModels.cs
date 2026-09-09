using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Request/response payloads for the Maxio Advanced Billing (Chargify) REST API.
// Property names are serialized with a snake_case naming policy to match the API.

public sealed class MaxioProductResponse
{
    public MaxioProduct Product { get; set; } = null!;
}

public sealed class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = default!;
    public DateTime? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
    public MaxioProductFamilyReference? ProductFamily { get; set; }
}

public sealed class MaxioProductFamilyReference
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
}

public sealed class MaxioCustomerResponse
{
    public MaxioCustomer Customer { get; set; } = null!;
}

public sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string Email { get; set; } = default!;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public sealed class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomerBody Customer { get; set; } = null!;
}

public sealed class MaxioCreateCustomerBody
{
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string Reference { get; set; } = default!;
}

public sealed class MaxioSubscriptionResponse
{
    public MaxioSubscription Subscription { get; set; } = null!;
}

public sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = default!;
    public int CustomerId { get; set; }
    public MaxioProduct? Product { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscriptionBody Subscription { get; set; } = null!;
}

public sealed class MaxioCreateSubscriptionBody
{
    public string ProductHandle { get; set; } = default!;
    public int CustomerId { get; set; }
    public string PaymentCollectionMethod { get; set; } = default!;
    public string UniquenessToken { get; set; } = default!;
}
