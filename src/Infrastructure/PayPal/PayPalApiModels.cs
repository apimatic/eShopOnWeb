using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Wire models for the PayPal REST API. Field names/nesting mirror the API exactly (snake_case).
// Null properties are omitted on serialization so the same objects serve raw-card and vaulted-card variants.

internal class PayPalOrderRequest
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "CAPTURE";
    [JsonPropertyName("payment_source")] public PayPalPaymentSource PaymentSource { get; set; } = new();
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnit> PurchaseUnits { get; set; } = new();
}

internal class PayPalPaymentSource
{
    [JsonPropertyName("card")] public PayPalCard? Card { get; set; }
    [JsonPropertyName("token")] public PayPalTokenRef? Token { get; set; }
}

internal class PayPalCard
{
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("billing_address")] public PayPalAddress? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
}

internal class PayPalTokenRef
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
}

internal class PayPalAddress
{
    [JsonPropertyName("address_line_1")] public string? Line1 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? City { get; set; }
    [JsonPropertyName("admin_area_1")] public string? State { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
}

internal class PayPalPurchaseUnit
{
    [JsonPropertyName("amount")] public PayPalAmount Amount { get; set; } = new();
}

internal class PayPalAmount
{
    [JsonPropertyName("currency_code")] public string CurrencyCode { get; set; } = "USD";
    [JsonPropertyName("value")] public string Value { get; set; } = "0.00";
}

internal class PayPalCustomer
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
}

// Vault setup-token / payment-token request bodies.
internal class PayPalVaultRequest
{
    [JsonPropertyName("payment_source")] public PayPalPaymentSource PaymentSource { get; set; } = new();
    [JsonPropertyName("customer")] public PayPalCustomer? Customer { get; set; }
}
