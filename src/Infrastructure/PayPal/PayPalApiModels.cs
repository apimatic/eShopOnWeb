using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Wire models for the PayPal REST API. Field names come verbatim from the OpenAPI specs under
// api-specs/paypal/ and are set explicitly so the JSON contract is unambiguous and spec-faithful.

// ---- Common ----------------------------------------------------------------

internal sealed class PayPalMoney
{
    [JsonPropertyName("currency_code")] public string CurrencyCode { get; set; } = "USD";
    [JsonPropertyName("value")] public string Value { get; set; } = "0.00";
}

internal sealed class PayPalAddress
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }   // city
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }   // state/province
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }  // required when address present
}

// ---- Checkout Orders v2: request ------------------------------------------

internal sealed class PayPalCardRequest
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }             // YYYY-MM
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("billing_address")] public PayPalAddress? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }          // for saved cards
}

internal sealed class PayPalPaymentSourceRequest
{
    [JsonPropertyName("card")] public PayPalCardRequest? Card { get; set; }
}

internal sealed class PayPalPurchaseUnitRequest
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney Amount { get; set; } = new();
}

internal sealed class PayPalCreateOrderRequest
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "CAPTURE";
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    [JsonPropertyName("payment_source")] public PayPalPaymentSourceRequest? PaymentSource { get; set; }
}

// ---- Checkout Orders v2: response -----------------------------------------

internal sealed class PayPalCaptureResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

internal sealed class PayPalPaymentCollectionResponse
{
    [JsonPropertyName("captures")] public List<PayPalCaptureResponse>? Captures { get; set; }
}

internal sealed class PayPalPurchaseUnitResponse
{
    [JsonPropertyName("payments")] public PayPalPaymentCollectionResponse? Payments { get; set; }
}

internal sealed class PayPalOrderResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnitResponse>? PurchaseUnits { get; set; }
}

// ---- Payments v2: refund ---------------------------------------------------

internal sealed class PayPalRefundResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

// ---- Vault v3: create payment token ---------------------------------------

internal sealed class PayPalVaultCustomer
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

internal sealed class PayPalVaultCardRequest
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("billing_address")] public PayPalAddress? BillingAddress { get; set; }
}

internal sealed class PayPalVaultPaymentSourceRequest
{
    [JsonPropertyName("card")] public PayPalVaultCardRequest? Card { get; set; }
}

internal sealed class PayPalCreatePaymentTokenRequest
{
    [JsonPropertyName("customer")] public PayPalVaultCustomer? Customer { get; set; }
    [JsonPropertyName("payment_source")] public PayPalVaultPaymentSourceRequest PaymentSource { get; set; } = new();
}

internal sealed class PayPalCardResponse
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
}

internal sealed class PayPalPaymentSourceResponse
{
    [JsonPropertyName("card")] public PayPalCardResponse? Card { get; set; }
}

internal sealed class PayPalPaymentTokenResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("customer")] public PayPalVaultCustomer? Customer { get; set; }
    [JsonPropertyName("payment_source")] public PayPalPaymentSourceResponse? PaymentSource { get; set; }
}

// ---- OAuth2 token ----------------------------------------------------------

internal sealed class PayPalOAuthTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

// ---- Error model (spec: error) --------------------------------------------

internal sealed class PayPalErrorDetail
{
    [JsonPropertyName("issue")] public string? Issue { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

internal sealed class PayPalErrorResponse
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<PayPalErrorDetail>? Details { get; set; }

    // OAuth token endpoint returns error/error_description instead of the REST error model.
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
}
