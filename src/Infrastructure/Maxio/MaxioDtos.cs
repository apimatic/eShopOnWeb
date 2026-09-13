using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioProductDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("trial_price_in_cents")]
    public long? TrialPriceInCents { get; set; }

    [JsonPropertyName("trial_interval")]
    public int? TrialInterval { get; set; }

    [JsonPropertyName("trial_interval_unit")]
    public string? TrialIntervalUnit { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("archived_at")]
    public string? ArchivedAt { get; set; }

    [JsonPropertyName("taxable")]
    public bool Taxable { get; set; }

    public decimal Price => PriceInCents / 100m;
}

public class MaxioCustomerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public class MaxioSubscriptionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("current_period_started_at")]
    public string? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public string? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public string? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public string? ActivatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioSubscriptionProductDto? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomerDto? Customer { get; set; }
}

public class MaxioSubscriptionProductDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    public decimal Price => PriceInCents / 100m;
}

public class MaxioCreateCustomerRequest
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

public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }

    [JsonPropertyName("customer_attributes")]
    public MaxioCustomerAttributes? CustomerAttributes { get; set; }

    [JsonPropertyName("payment_profile_id")]
    public int? PaymentProfileId { get; set; }

    [JsonPropertyName("credit_card_attributes")]
    public MaxioCreditCardAttributes? CreditCardAttributes { get; set; }
}

public class MaxioCreditCardAttributes
{
    [JsonPropertyName("chargify_token")]
    public string ChargifyToken { get; set; } = string.Empty;

    [JsonPropertyName("payment_type")]
    public string PaymentType { get; set; } = "credit_card";
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

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }
}

// Wrapper types matching Maxio JSON envelope
public class MaxioListResponse<T>
{
    [JsonPropertyName("items")]
    public object[]? Items { get; set; }
}

// Individual product wrapper
public class MaxioProductWrapper
{
    [JsonPropertyName("product")]
    public MaxioProductDto Product { get; set; } = null!;
}

// Individual customer wrapper
public class MaxioCustomerWrapper
{
    [JsonPropertyName("customer")]
    public MaxioCustomerDto Customer { get; set; } = null!;
}

// Individual subscription wrapper
public class MaxioSubscriptionWrapper
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionDto Subscription { get; set; } = null!;
}

// Subscription create wrapper
public class MaxioSubscriptionCreateWrapper
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscriptionRequest Subscription { get; set; } = null!;
}

// Customer create wrapper
public class MaxioCustomerCreateWrapper
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomerRequest Customer { get; set; } = null!;
}

// Payment Profile DTOs
public class MaxioPaymentProfileDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("payment_type")]
    public string PaymentType { get; set; } = string.Empty;

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("masked_card_number")]
    public string? MaskedCardNumber { get; set; }

    [JsonPropertyName("card_type")]
    public string? CardType { get; set; }
}

public class MaxioCreatePaymentProfileRequest
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("card_type")]
    public string? CardType { get; set; }

    [JsonPropertyName("card_number")]
    public string CardNumber { get; set; } = string.Empty;

    [JsonPropertyName("expiration_month")]
    public int ExpirationMonth { get; set; }

    [JsonPropertyName("expiration_year")]
    public int ExpirationYear { get; set; }

    [JsonPropertyName("billing_address")]
    public string? BillingAddress { get; set; }

    [JsonPropertyName("billing_city")]
    public string? BillingCity { get; set; }

    [JsonPropertyName("billing_state")]
    public string? BillingState { get; set; }

    [JsonPropertyName("billing_zip")]
    public string? BillingZip { get; set; }

    [JsonPropertyName("billing_country")]
    public string? BillingCountry { get; set; }
}

public class MaxioPaymentProfileWrapper
{
    [JsonPropertyName("payment_profile")]
    public MaxioPaymentProfileDto PaymentProfile { get; set; } = null!;
}

public class MaxioPaymentProfileCreateWrapper
{
    [JsonPropertyName("payment_profile")]
    public MaxioCreatePaymentProfileRequest PaymentProfile { get; set; } = null!;
}
