using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// DTOs for the Maxio Advanced Billing REST API. Field names are snake_case on the wire and are
/// mapped via JsonNamingPolicy.SnakeCaseLower.
/// </summary>

public class MaxioProductFamily
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public int ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? Reference { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

public class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer? Customer { get; set; }
}

public class MaxioCreateCustomer
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription? Subscription { get; set; }
}

public class MaxioCreateSubscription
{
    public string? ProductHandle { get; set; }
    public string? CustomerReference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}
