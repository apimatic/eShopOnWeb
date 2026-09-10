namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// These types mirror the JSON shapes returned by the Maxio Advanced Billing REST API.
// Property names are mapped from snake_case via JsonNamingPolicy.SnakeCaseLower configured
// on the client's serializer options (see MaxioApiClient).

internal sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
}

internal sealed class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? Currency { get; set; }
    public long? ProductPriceInCents { get; set; }
    public string? Reference { get; set; }

    public System.DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public System.DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public System.DateTimeOffset? NextAssessmentAt { get; set; }
    public System.DateTimeOffset? CreatedAt { get; set; }

    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}
