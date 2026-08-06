using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

// Wire models that mirror the PayPal OpenAPI specs (Checkout Orders v2, Payments v2,
// Vault Payment Tokens v3). Property names are pinned with [JsonPropertyName] to match the
// spec exactly. Only the fields this integration reads/writes are modelled.

// ---- Checkout Orders v2: create order request -------------------------------------------

internal sealed class CreateOrderRequest
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "CAPTURE";
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    [JsonPropertyName("payment_source")] public OrderPaymentSourceRequest? PaymentSource { get; set; }
}

internal sealed class PurchaseUnitRequest
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("amount")] public AmountRequest Amount { get; set; } = new();
}

internal sealed class AmountRequest
{
    [JsonPropertyName("currency_code")] public string CurrencyCode { get; set; } = "USD";
    [JsonPropertyName("value")] public string Value { get; set; } = "0.00";
}

internal sealed class OrderPaymentSourceRequest
{
    [JsonPropertyName("card")] public OrderCardRequest? Card { get; set; }
}

internal sealed class OrderCardRequest
{
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("billing_address")] public CardBillingAddressModel? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
}

internal sealed class CardBillingAddressModel
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string CountryCode { get; set; } = string.Empty;
}

// ---- Checkout Orders v2: order response -------------------------------------------------

internal sealed class OrderResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }
}

internal sealed class PurchaseUnitResponse
{
    [JsonPropertyName("payments")] public PaymentsResponse? Payments { get; set; }
}

internal sealed class PaymentsResponse
{
    [JsonPropertyName("captures")] public List<CaptureResponse>? Captures { get; set; }
}

internal sealed class CaptureResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

// ---- Payments v2: refund ----------------------------------------------------------------

internal sealed class RefundResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

// ---- Vault Payment Tokens v3 ------------------------------------------------------------

internal sealed class VaultTokenRequest
{
    [JsonPropertyName("customer")] public VaultCustomerModel? Customer { get; set; }
    [JsonPropertyName("payment_source")] public VaultPaymentSourceRequest PaymentSource { get; set; } = new();
}

internal sealed class VaultCustomerModel
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("merchant_customer_id")] public string? MerchantCustomerId { get; set; }
}

internal sealed class VaultPaymentSourceRequest
{
    [JsonPropertyName("card")] public VaultCardRequest? Card { get; set; }
}

internal sealed class VaultCardRequest
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("billing_address")] public CardBillingAddressModel? BillingAddress { get; set; }
}

internal sealed class VaultTokenResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("customer")] public VaultCustomerModel? Customer { get; set; }
    [JsonPropertyName("payment_source")] public VaultPaymentSourceResponse? PaymentSource { get; set; }
}

internal sealed class VaultPaymentSourceResponse
{
    [JsonPropertyName("card")] public VaultCardResponse? Card { get; set; }
}

internal sealed class VaultCardResponse
{
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
}

// ---- OAuth token ------------------------------------------------------------------------

internal sealed class OAuthTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

// ---- Error model (shared across specs) --------------------------------------------------

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
