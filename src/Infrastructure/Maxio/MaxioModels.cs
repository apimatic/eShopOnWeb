using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire models for the Maxio Advanced Billing API, mirroring the schemas of
// maxio-spec/openapi.yaml (Customer, Product, Subscription and their create
// request schemas). JSON is serialized/deserialized with a snake_case naming
// policy, matching the API's property names.

public sealed class MaxioCustomer
{
    public int Id { get; set; }

    public string? Reference { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Organization { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

public sealed class MaxioProductFamily
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }
}

public sealed class MaxioProduct
{
    public int Id { get; set; }

    public string? Handle { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public long? PriceInCents { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public bool? Taxable { get; set; }

    public bool? RequireCreditCard { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public string? ProductPricePointName { get; set; }

    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioSubscription
{
    public int Id { get; set; }

    public string? State { get; set; }

    public string? Reference { get; set; }

    public long? BalanceInCents { get; set; }

    public long? ProductPriceInCents { get; set; }

    public DateTime? CurrentPeriodStartsAt { get; set; }

    public DateTime? CurrentPeriodEndsAt { get; set; }

    public DateTime? NextAssessmentAt { get; set; }

    public DateTime? ActivatedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? CanceledAt { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public MaxioCustomer? Customer { get; set; }

    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

public sealed class MaxioCreateCustomerBody
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomerBody Customer { get; set; } = new();
}

public sealed class MaxioCreateSubscriptionBody
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    /// <summary>
    /// "remittance" enrolls the subscriber without requiring a card on file
    /// (invoice-style collection), per the spec's Collection-Method enum.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

public sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscriptionBody Subscription { get; set; } = new();
}

public sealed class MaxioErrorListResponse
{
    public List<string>? Errors { get; set; }
}
