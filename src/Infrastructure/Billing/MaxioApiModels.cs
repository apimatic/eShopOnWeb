using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioCustomer
{
    public long? Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public sealed class MaxioCreateCustomerRequest
{
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioProduct
{
    public long? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioProductFamily
{
    public long? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

public sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

public sealed class MaxioSubscription
{
    public long? Id { get; set; }
    public string? State { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public string? Reference { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

public sealed class MaxioCreateSubscription
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

public sealed class MaxioErrorResponse
{
    public object? Errors { get; set; }
    public string? Error { get; set; }
}
