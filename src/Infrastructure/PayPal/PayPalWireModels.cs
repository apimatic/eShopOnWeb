using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Wire DTOs mirroring the PayPal OpenAPI schemas used by this integration. Property names map to
// the spec's snake_case fields via the shared snake_case naming policy. Only the fields this
// integration reads or sends are modelled; the spec remains the authoritative contract.

internal sealed class Money
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

// ---- Checkout Orders v2 ----

internal sealed class CreateOrderRequest
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();
}

internal sealed class PurchaseUnitRequest
{
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public Money Amount { get; set; } = new();
}

internal sealed class AuthorizeOrderRequest
{
    public PaymentSourceRequest? PaymentSource { get; set; }
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
    public CardBillingAddressWire? BillingAddress { get; set; }
    public string? VaultId { get; set; }
    public CardAttributesRequest? Attributes { get; set; }
}

internal sealed class CardAttributesRequest
{
    public VaultAttributeRequest? Vault { get; set; }
    public CustomerAttributeRequest? Customer { get; set; }
}

internal sealed class VaultAttributeRequest
{
    public string? StoreInVault { get; set; } // "ON_SUCCESS"
}

internal sealed class CustomerAttributeRequest
{
    public string? Id { get; set; }
}

internal sealed class CardBillingAddressWire
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
    public string? Id { get; set; }
    public string? Status { get; set; }
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
    public CardResponseAttributes? Attributes { get; set; }
}

internal sealed class CardResponseAttributes
{
    public VaultResponse? Vault { get; set; }
}

internal sealed class VaultResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public VaultCustomerResponse? Customer { get; set; }
}

internal sealed class VaultCustomerResponse
{
    public string? Id { get; set; }
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

// ---- Payments v2 ----

internal sealed class AuthorizationResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public Money? Amount { get; set; }
    public string? ExpirationTime { get; set; }
}

internal sealed class CaptureRequest
{
    public Money? Amount { get; set; }
    public bool? FinalCapture { get; set; }
}

internal sealed class ReauthorizeRequest
{
    public Money? Amount { get; set; }
}

internal sealed class CaptureResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public Money? Amount { get; set; }
    public SellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal sealed class SellerReceivableBreakdown
{
    public Money? GrossAmount { get; set; }
    public Money? PaypalFee { get; set; }
    public Money? NetAmount { get; set; }
}

internal sealed class RefundRequest
{
    public Money? Amount { get; set; }
}

internal sealed class RefundResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public Money? Amount { get; set; }
}

// ---- OAuth ----

internal sealed class TokenResponse
{
    public string? AccessToken { get; set; }
    public string? TokenType { get; set; }
    public int ExpiresIn { get; set; }
}

// ---- Vault Payment Tokens v3 ----

internal sealed class VaultTokenRequest
{
    public VaultCustomerRequest? Customer { get; set; }
    public VaultPaymentSourceRequest PaymentSource { get; set; } = new();
}

internal sealed class VaultCustomerRequest
{
    public string? Id { get; set; }
}

internal sealed class VaultPaymentSourceRequest
{
    public CardRequest? Card { get; set; }
}

internal sealed class VaultTokenResponse
{
    public string? Id { get; set; }
    public VaultCustomerResponse? Customer { get; set; }
    public VaultPaymentSourceResponse? PaymentSource { get; set; }
}

internal sealed class VaultPaymentSourceResponse
{
    public CardResponse? Card { get; set; }
}

internal sealed class CustomerVaultTokensResponse
{
    public int? TotalItems { get; set; }
    public int? TotalPages { get; set; }
    public List<VaultTokenResponse>? PaymentTokens { get; set; }
}

// ---- Transaction Search v1 ----

internal sealed class TransactionSearchResponse
{
    public List<TransactionDetail>? TransactionDetails { get; set; }
    public int? Page { get; set; }
    public int? TotalItems { get; set; }
    public int? TotalPages { get; set; }
}

internal sealed class TransactionDetail
{
    public TransactionInfo? TransactionInfo { get; set; }
}

internal sealed class TransactionInfo
{
    public string? TransactionId { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public string? TransactionInitiationDate { get; set; }
    public Money? TransactionAmount { get; set; }
    public Money? FeeAmount { get; set; }
    public string? InvoiceId { get; set; }
}

// ---- Error model ----

internal sealed class PayPalErrorResponse
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public string? Error { get; set; }             // OAuth-style error
    public string? ErrorDescription { get; set; }  // OAuth-style error description
    public List<PayPalErrorDetail>? Details { get; set; }
}

internal sealed class PayPalErrorDetail
{
    public string? Issue { get; set; }
    public string? Description { get; set; }
    public string? Field { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object>? Extra { get; set; }
}
