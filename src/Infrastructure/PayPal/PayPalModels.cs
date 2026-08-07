using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Wire models mirroring the PayPal OpenAPI specs under api-specs/paypal/. Property names are set
// explicitly to match the spec exactly (PayPal uses snake_case with numeric suffixes such as
// admin_area_2 that a naming policy would not reproduce). Only the fields this integration uses are
// modelled; unknown response fields are ignored by the deserializer.

// ----- OAuth (v1/oauth2/token) ---------------------------------------------------------------------

internal sealed class PayPalTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

// ----- Checkout Orders v2 (create + capture) -------------------------------------------------------

internal sealed class OrderRequest
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "CAPTURE";
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    [JsonPropertyName("payment_source")] public PaymentSourceRequest? PaymentSource { get; set; }
}

internal sealed class PurchaseUnitRequest
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("amount")] public AmountRequest Amount { get; set; } = new();
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

internal sealed class AmountRequest
{
    [JsonPropertyName("currency_code")] public string CurrencyCode { get; set; } = "USD";
    [JsonPropertyName("value")] public string Value { get; set; } = "0.00";
}

internal sealed class PaymentSourceRequest
{
    [JsonPropertyName("card")] public CardRequest? Card { get; set; }
}

internal sealed class CardRequest
{
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("billing_address")] public AddressRequest? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
}

internal sealed class AddressRequest
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
}

internal sealed class OrderResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }
    [JsonPropertyName("payment_source")] public PaymentSourceResponse? PaymentSource { get; set; }
}

internal sealed class PurchaseUnitResponse
{
    [JsonPropertyName("payments")] public PaymentCollectionResponse? Payments { get; set; }
}

internal sealed class PaymentCollectionResponse
{
    [JsonPropertyName("captures")] public List<CaptureResponse>? Captures { get; set; }
}

internal sealed class CaptureResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

internal sealed class PaymentSourceResponse
{
    [JsonPropertyName("card")] public CardResponse? Card { get; set; }
}

internal sealed class CardResponse
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
}

// ----- Payments v2 (refund) ------------------------------------------------------------------------

internal sealed class RefundResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

// ----- Vault Payment Tokens v3 ---------------------------------------------------------------------

internal sealed class VaultPaymentTokenRequest
{
    [JsonPropertyName("payment_source")] public VaultPaymentSourceRequest PaymentSource { get; set; } = new();
    [JsonPropertyName("customer")] public VaultCustomer? Customer { get; set; }
}

internal sealed class VaultPaymentSourceRequest
{
    [JsonPropertyName("card")] public CardRequest? Card { get; set; }
}

internal sealed class VaultCustomer
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

internal sealed class VaultPaymentTokenResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("payment_source")] public PaymentSourceResponse? PaymentSource { get; set; }
    [JsonPropertyName("customer")] public VaultCustomer? Customer { get; set; }
}

// ----- Error model (shared) ------------------------------------------------------------------------

internal sealed class PayPalErrorResponse
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<PayPalErrorDetail>? Details { get; set; }
}

internal sealed class PayPalErrorDetail
{
    [JsonPropertyName("field")] public string? Field { get; set; }
    [JsonPropertyName("issue")] public string? Issue { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}
