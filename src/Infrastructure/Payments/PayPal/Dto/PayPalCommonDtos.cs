using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal.Dto;

// DTOs mirroring the PayPal OpenAPI schemas in api-specs/paypal (snake_case on the wire).

internal sealed class PayPalMoney
{
    [JsonPropertyName("currency_code")] public string? CurrencyCode { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
}

internal sealed class PayPalAddress
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
}

internal sealed class PayPalStoredCredential
{
    [JsonPropertyName("payment_initiator")] public string? PaymentInitiator { get; set; }
    [JsonPropertyName("payment_type")] public string? PaymentType { get; set; }
    [JsonPropertyName("usage")] public string? Usage { get; set; }
}

/// <summary>card_request schema (checkout_orders_v2).</summary>
internal sealed class PayPalCardRequest
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("billing_address")] public PayPalAddress? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
    [JsonPropertyName("stored_credential")] public PayPalStoredCredential? StoredCredential { get; set; }
}

internal sealed class PayPalPaymentSourceRequest
{
    [JsonPropertyName("card")] public PayPalCardRequest? Card { get; set; }
}

/// <summary>card_response schema (safe display metadata only).</summary>
internal sealed class PayPalCardResponse
{
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
}

internal sealed class PayPalPaymentSourceResponse
{
    [JsonPropertyName("card")] public PayPalCardResponse? Card { get; set; }
}

/// <summary>error schema shared by the PayPal APIs.</summary>
internal sealed class PayPalErrorResponse
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<PayPalErrorDetail>? Details { get; set; }
}

internal sealed class PayPalErrorDetail
{
    [JsonPropertyName("issue")] public string? Issue { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("field")] public string? Field { get; set; }
}

internal sealed class PayPalTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}
