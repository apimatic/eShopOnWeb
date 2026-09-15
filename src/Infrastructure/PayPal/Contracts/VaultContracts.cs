using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;

// --- POST /v3/vault/payment-tokens (vault_payment_tokens_v3) ---

public class PaymentTokenRequest
{
    [JsonPropertyName("customer")]
    public VaultCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public VaultPaymentSource PaymentSource { get; set; } = new();
}

public class VaultCustomer
{
    /// <summary>PayPal-generated customer id (present on subsequent vaults for the same shopper).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The merchant's own customer id.</summary>
    [JsonPropertyName("merchant_customer_id")]
    public string? MerchantCustomerId { get; set; }
}

public class VaultPaymentSource
{
    [JsonPropertyName("card")]
    public VaultCard? Card { get; set; }
}

/// <summary>Raw card for direct vaulting (request) or safe descriptor (response).</summary>
public class VaultCard
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    // Request-only raw fields:
    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; } // YYYY-MM

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("billing_address")]
    public AddressPortable? BillingAddress { get; set; }

    // Response-only safe descriptor fields:
    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    /// <summary>Funding type (CREDIT/DEBIT/...), literally named "type" in the response.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class PaymentTokenResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; } // the vault id

    [JsonPropertyName("customer")]
    public VaultCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public VaultPaymentSource? PaymentSource { get; set; }
}
