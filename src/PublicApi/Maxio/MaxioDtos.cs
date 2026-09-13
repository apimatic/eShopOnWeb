using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// --- Product / Plan DTOs ---

public class ProductItem
{
    [JsonPropertyName("product")]
    public ProductDto? Product { get; set; }
}

public class ProductDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public string? IntervalUnit { get; set; }
    public int Interval { get; set; }
    public bool Taxable { get; set; }
    public bool RequireCreditCard { get; set; }
}

// --- Customer DTOs ---

public class CustomerLookupResponse
{
    [JsonPropertyName("customer")]
    public CustomerDto? Customer { get; set; }
}

public class CreateCustomerRequest
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

public class CreateCustomerResponse
{
    [JsonPropertyName("customer")]
    public CustomerDto? Customer { get; set; }
}

public class CustomerDto
{
    public int Id { get; set; }
    public string? First_name { get; set; }
    public string? Last_name { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

// --- Subscription DTOs ---

public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }

    [JsonPropertyName("customer_attributes")]
    public MaxioCustomerAttributes? CustomerAttributes { get; set; }

    [JsonPropertyName("credit_card_attributes")]
    public MaxioCreditCardAttributes? CreditCardAttributes { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}

public class MaxioCreditCardAttributes
{
    [JsonPropertyName("payment_type")]
    public string PaymentType { get; set; } = "credit_card";

    [JsonPropertyName("full_number")]
    public string? FullNumber { get; set; }

    [JsonPropertyName("expiration_month")]
    public string? ExpirationMonth { get; set; }

    [JsonPropertyName("expiration_year")]
    public string? ExpirationYear { get; set; }
}

public class MaxioCustomerAttributes
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

public class MaxioCreateSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public SubscriptionDto? Subscription { get; set; }
}

public class SubscriptionListItem
{
    [JsonPropertyName("subscription")]
    public SubscriptionDto? Subscription { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long Balance_in_cents { get; set; }
    public long Total_revenue_in_cents { get; set; }
    public long Product_price_in_cents { get; set; }
    public string? Current_period_ends_at { get; set; }
    public string? Next_assessment_at { get; set; }
    public string? Activated_at { get; set; }
    public string? Created_at { get; set; }
    public bool? Cancel_at_end_of_period { get; set; }
    public string? Canceled_at { get; set; }
    public SubscriptionProduct? Product { get; set; }
    public SubscriptionCustomer? Customer { get; set; }
}

public class SubscriptionProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

public class SubscriptionCustomer
{
    public int Id { get; set; }
    public string? First_name { get; set; }
    public string? Last_name { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}
