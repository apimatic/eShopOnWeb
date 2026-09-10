namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Request payloads. Serialized with JsonNamingPolicy.SnakeCaseLower and
// DefaultIgnoreCondition = WhenWritingNull so only populated fields are sent.

internal sealed class CreateCustomerRequest
{
    public CustomerInput Customer { get; set; } = new();
}

internal sealed class CustomerInput
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    public SubscriptionInput Subscription { get; set; } = new();
}

internal sealed class SubscriptionInput
{
    public string? ProductHandle { get; set; }
    public long CustomerId { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}
