using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Payload/response models for the Maxio Advanced Billing (Billing API) endpoints used
// by this integration. Property names follow the API's snake_case JSON via the
// serializer's naming policy (see MaxioBillingClient.JsonOptions).

public class MaxioProductFamilyRef
{
    public int Id { get; set; }

    public string? Handle { get; set; }

    public string? Name { get; set; }
}

public class MaxioProduct
{
    public int Id { get; set; }

    public string? Handle { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public long PriceInCents { get; set; }

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public bool RequireCreditCard { get; set; }

    public bool Taxable { get; set; }

    public string? ArchivedAt { get; set; }

    public MaxioProductFamilyRef? ProductFamily { get; set; }
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

    public long ProductPriceInCents { get; set; }

    public long BalanceInCents { get; set; }

    public string? CurrentPeriodEndsAt { get; set; }

    public string? NextAssessmentAt { get; set; }

    public string? ActivatedAt { get; set; }

    public string? CreatedAt { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }

    public string? CanceledAt { get; set; }

    public MaxioCustomer? Customer { get; set; }

    public MaxioProduct? Product { get; set; }
}

public class MaxioCustomerCreateRequest
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }
}

public class MaxioSubscriptionCreateRequest
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";
}