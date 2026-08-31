using System.Text.Json.Serialization;

// DTOs for the Payment Method Tokens (Vault) API v3 (api-specs/paypal/vault_payment_tokens_v3).
namespace Microsoft.eShopWeb.Infrastructure.Payments.Dto;

/// <summary>payment_token_request schema.</summary>
public class PayPalPaymentTokenRequest
{
    [JsonPropertyName("customer")]
    public PayPalVaultCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalVaultPaymentSource PaymentSource { get; set; } = new();
}

/// <summary>customer schema — associates the vaulted instrument with a merchant-side customer.</summary>
public class PayPalVaultCustomer
{
    [JsonPropertyName("merchant_customer_id")]
    public string? MerchantCustomerId { get; set; }
}

/// <summary>Payment Token Request Payment Source schema (card only).</summary>
public class PayPalVaultPaymentSource
{
    [JsonPropertyName("card")]
    public PayPalVaultCardRequest Card { get; set; } = new();
}

/// <summary>Payment Token Request Card schema.</summary>
public class PayPalVaultCardRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("expiry")]
    public string Expiry { get; set; } = string.Empty;

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("billing_address")]
    public PayPalAddress? BillingAddress { get; set; }
}

/// <summary>payment_token_response schema (subset).</summary>
public class PayPalPaymentTokenResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("payment_source")]
    public PayPalPaymentTokenResponseSource? PaymentSource { get; set; }
}

/// <summary>Payment Token Response Payment Source schema (card only).</summary>
public class PayPalPaymentTokenResponseSource
{
    [JsonPropertyName("card")]
    public PayPalVaultCardResponse? Card { get; set; }
}

/// <summary>card_response schema (safe display data only).</summary>
public class PayPalVaultCardResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }
}
