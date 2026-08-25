using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Wire;

// Wire-format DTOs matching the PayPal REST APIs (Orders v2, Payments v2, Vault v3,
// Transaction Search v1) exactly as documented. Kept private to the Infrastructure layer;
// PayPalClient translates to/from the ApplicationCore.PayPal DTOs.

internal class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal class Money
{
    public Money() { }
    public Money(string currencyCode, string value) { CurrencyCode = currencyCode; Value = value; }

    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

internal class AddressWire
{
    [JsonPropertyName("country_code")]
    public string CountryCode { get; set; } = string.Empty;

    [JsonPropertyName("address_line_1")]
    public string? AddressLine1 { get; set; }

    [JsonPropertyName("address_line_2")]
    public string? AddressLine2 { get; set; }

    [JsonPropertyName("admin_area_2")]
    public string? AdminArea2 { get; set; } // city

    [JsonPropertyName("admin_area_1")]
    public string? AdminArea1 { get; set; } // state

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; set; }
}

// ---- Create Order (POST /v2/checkout/orders) ----

internal class CreateOrderRequest
{
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "AUTHORIZE";

    [JsonPropertyName("purchase_units")]
    public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();
}

internal class PurchaseUnitRequest
{
    [JsonPropertyName("amount")]
    public Money Amount { get; set; } = new();
}

internal class OrderResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("purchase_units")]
    public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }

    [JsonPropertyName("links")]
    public List<LinkDescription>? Links { get; set; }
}

internal class PurchaseUnitResponse
{
    [JsonPropertyName("payments")]
    public PaymentCollection? Payments { get; set; }
}

internal class PaymentCollection
{
    [JsonPropertyName("authorizations")]
    public List<AuthorizationResponse>? Authorizations { get; set; }

    [JsonPropertyName("captures")]
    public List<CaptureResponse>? Captures { get; set; }
}

internal class LinkDescription
{
    [JsonPropertyName("rel")]
    public string Rel { get; set; } = string.Empty;

    [JsonPropertyName("href")]
    public string Href { get; set; } = string.Empty;
}

// ---- Authorize Order (POST /v2/checkout/orders/{id}/authorize) ----

internal class AuthorizeOrderRequest
{
    [JsonPropertyName("payment_source")]
    public PaymentSourceRequest PaymentSource { get; set; } = new();
}

internal class PaymentSourceRequest
{
    [JsonPropertyName("card")]
    public CardRequest? Card { get; set; }
}

internal class CardRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("billing_address")]
    public AddressWire? BillingAddress { get; set; }

    [JsonPropertyName("vault_id")]
    public string? VaultId { get; set; }
}

// ---- Authorization resource (Payments v2) ----

internal class AuthorizationResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public Money? Amount { get; set; }

    [JsonPropertyName("expiration_time")]
    public string? ExpirationTime { get; set; }
}

internal class ReauthorizeRequest
{
    [JsonPropertyName("amount")]
    public Money Amount { get; set; } = new();
}

// ---- Capture (Payments v2) ----

internal class CaptureResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public Money? Amount { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public SellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal class SellerReceivableBreakdown
{
    [JsonPropertyName("paypal_fee")]
    public Money? PayPalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public Money? NetAmount { get; set; }
}

// ---- Refund (Payments v2) ----

internal class RefundRequest
{
    [JsonPropertyName("amount")]
    public Money? Amount { get; set; }
}

internal class RefundResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public Money? Amount { get; set; }
}

// ---- Vault (v3) ----

internal class CreateVaultPaymentTokenRequest
{
    [JsonPropertyName("payment_source")]
    public VaultPaymentSourceRequest PaymentSource { get; set; } = new();

    [JsonPropertyName("customer")]
    public VaultCustomerRequest? Customer { get; set; }
}

internal class VaultPaymentSourceRequest
{
    [JsonPropertyName("card")]
    public VaultCardRequest Card { get; set; } = new();
}

internal class VaultCardRequest
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
    public AddressWire? BillingAddress { get; set; }
}

internal class VaultCustomerRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

internal class PaymentTokenResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("payment_source")]
    public PaymentTokenSource? PaymentSource { get; set; }
}

internal class PaymentTokenSource
{
    [JsonPropertyName("card")]
    public PaymentTokenCard? Card { get; set; }
}

internal class PaymentTokenCard
{
    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }
}

// ---- Transaction Search (v1) ----

internal class TransactionSearchResponse
{
    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("transaction_details")]
    public List<TransactionDetail>? TransactionDetails { get; set; }
}

internal class TransactionDetail
{
    [JsonPropertyName("transaction_info")]
    public TransactionInfo? TransactionInfo { get; set; }
}

internal class TransactionInfo
{
    [JsonPropertyName("transaction_id")]
    public string TransactionId { get; set; } = string.Empty;

    [JsonPropertyName("transaction_event_code")]
    public string? TransactionEventCode { get; set; }

    [JsonPropertyName("transaction_initiation_date")]
    public string? TransactionInitiationDate { get; set; }

    [JsonPropertyName("transaction_amount")]
    public Money? TransactionAmount { get; set; }

    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }
}

// ---- Error response ----

internal class PayPalErrorResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("debug_id")]
    public string? DebugId { get; set; }

    [JsonPropertyName("details")]
    public List<PayPalErrorDetail>? Details { get; set; }
}

internal class PayPalErrorDetail
{
    [JsonPropertyName("issue")]
    public string? Issue { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
