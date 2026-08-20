using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Models;

public sealed class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public sealed class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}

public sealed class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public sealed class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? ProductPricePointName { get; set; }
    public string? ArchivedAt { get; set; }
}

public sealed class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

public sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

public sealed class MaxioCreateSubscription
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

public sealed class MaxioErrorResponse
{
    [JsonConverter(typeof(MaxioErrorListConverter))]
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}
