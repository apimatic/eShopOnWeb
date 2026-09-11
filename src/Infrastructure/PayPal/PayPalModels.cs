using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Wire models for the PayPal REST APIs, shaped to the OpenAPI documents in api-specs/paypal.
// Property names are PascalCase and serialized to snake_case via the shared JsonSerializerOptions.
// Only the fields this integration reads/writes are modelled; unknown fields are ignored on read.

// ---- OAuth ------------------------------------------------------------------------------------
internal sealed class TokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("token_type")] public string TokenType { get; set; } = string.Empty;
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

// ---- Money ------------------------------------------------------------------------------------
internal sealed class Money
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

// ---- Checkout Orders v2 (create order = authorize) -------------------------------------------
internal sealed class CreateOrderRequest
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    public PaymentSourceRequest? PaymentSource { get; set; }
}

internal sealed class PurchaseUnitRequest
{
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public Money Amount { get; set; } = new();
}

internal sealed class PaymentSourceRequest
{
    public CardRequest? Card { get; set; }
}

internal sealed class CardRequest
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddress? BillingAddress { get; set; }
    public string? VaultId { get; set; }
}

internal sealed class BillingAddress
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

internal sealed class OrderResponse
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public PaymentSourceResponse? PaymentSource { get; set; }
    public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }
}

internal sealed class PaymentSourceResponse
{
    public CardResponse? Card { get; set; }
}

internal sealed class CardResponse
{
    public string? Name { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

internal sealed class PurchaseUnitResponse
{
    public PaymentCollection? Payments { get; set; }
}

internal sealed class PaymentCollection
{
    public List<AuthorizationResponse>? Authorizations { get; set; }
    public List<CaptureResponse>? Captures { get; set; }
    public List<RefundResponse>? Refunds { get; set; }
}

// ---- Payments v2 (authorizations / captures / refunds) ---------------------------------------
internal sealed class AuthorizationResponse
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Money? Amount { get; set; }
    public DateTimeOffset? ExpirationTime { get; set; }
}

internal sealed class CaptureRequest
{
    public bool FinalCapture { get; set; } = true;
}

internal sealed class CaptureResponse
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Money? Amount { get; set; }
    public SellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal sealed class SellerReceivableBreakdown
{
    public Money? GrossAmount { get; set; }
    public Money? PaypalFee { get; set; }
    public Money? NetAmount { get; set; }
}

internal sealed class ReauthorizeRequest
{
    public Money Amount { get; set; } = new();
}

internal sealed class RefundRequest
{
    public Money? Amount { get; set; }
    public string? InvoiceId { get; set; }
}

internal sealed class RefundResponse
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Money? Amount { get; set; }
    public SellerPayableBreakdown? SellerPayableBreakdown { get; set; }
}

internal sealed class SellerPayableBreakdown
{
    public Money? GrossAmount { get; set; }
    public Money? TotalRefundedAmount { get; set; }
}

// ---- Vault v3 --------------------------------------------------------------------------------
internal sealed class VaultPaymentTokenRequest
{
    public VaultPaymentSource PaymentSource { get; set; } = new();
}

internal sealed class VaultPaymentSource
{
    public CardRequest Card { get; set; } = new();
}

internal sealed class VaultPaymentTokenResponse
{
    public string Id { get; set; } = string.Empty;
    public VaultCustomer? Customer { get; set; }
    public PaymentSourceResponse? PaymentSource { get; set; }
}

internal sealed class VaultCustomer
{
    public string? Id { get; set; }
}

// ---- Transaction Search v1 -------------------------------------------------------------------
internal sealed class SearchResponse
{
    public List<TransactionDetail>? TransactionDetails { get; set; }
    public int Page { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

internal sealed class TransactionDetail
{
    public TransactionInfo? TransactionInfo { get; set; }
}

internal sealed class TransactionInfo
{
    public string? TransactionId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public Money? TransactionAmount { get; set; }
    public Money? FeeAmount { get; set; }
    public string? TransactionStatus { get; set; }
    public string? TransactionEventCode { get; set; }
    public DateTimeOffset? TransactionInitiationDate { get; set; }
}

// ---- Error model -----------------------------------------------------------------------------
internal sealed class PayPalErrorResponse
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public List<PayPalErrorDetail>? Details { get; set; }
}

internal sealed class PayPalErrorDetail
{
    public string? Field { get; set; }
    public string? Issue { get; set; }
    public string? Description { get; set; }
}
