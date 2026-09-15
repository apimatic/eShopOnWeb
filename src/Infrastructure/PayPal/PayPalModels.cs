using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Wire models for PayPal's REST APIs. Property names are PascalCase and mapped to the spec's
// snake_case by a SnakeCaseLower naming policy (see PayPalClient). Only the fields this
// integration needs are modelled; unknown fields are ignored on read.

internal sealed class TokenResponse
{
    public string? AccessToken { get; set; }
    public string? TokenType { get; set; }
    public int ExpiresIn { get; set; }
}

internal sealed class Money
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal sealed class AddressPortable
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; } // city
    public string? AdminArea1 { get; set; } // state / province
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

internal sealed class VaultInstruction
{
    public string? StoreInVault { get; set; } // "ON_SUCCESS"
}

internal sealed class CardAttributes
{
    public VaultInstruction? Vault { get; set; }
}

internal sealed class CardRequest
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; } // YYYY-MM
    public string? SecurityCode { get; set; }
    public AddressPortable? BillingAddress { get; set; }
    public string? VaultId { get; set; }
    public CardAttributes? Attributes { get; set; }
}

internal sealed class TokenIdRequest
{
    public string? Id { get; set; }
    public string? Type { get; set; } // SETUP_TOKEN
}

internal sealed class PaymentSourceRequest
{
    public CardRequest? Card { get; set; }
}

internal sealed class PurchaseUnitRequest
{
    public Money Amount { get; set; } = new();
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public string? Description { get; set; }
}

internal sealed class OrderRequest
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    public PaymentSourceRequest? PaymentSource { get; set; }
}

internal sealed class CaptureRequest
{
    public Money? Amount { get; set; }
    public bool? FinalCapture { get; set; }
    public string? InvoiceId { get; set; }
}

internal sealed class ReauthorizeRequest
{
    public Money? Amount { get; set; }
}

internal sealed class RefundRequest
{
    public Money? Amount { get; set; }
    public string? InvoiceId { get; set; }
}

// --- Vault ---

internal sealed class VaultPaymentSource
{
    public CardRequest? Card { get; set; }
    public TokenIdRequest? Token { get; set; }
}

internal sealed class PaymentTokenRequest
{
    public VaultPaymentSource PaymentSource { get; set; } = new();
}

// --- Responses ---

internal sealed class LinkDescription
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}

internal sealed class CardResponse
{
    public string? Name { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? Type { get; set; }
}

internal sealed class PaymentSourceResponse
{
    public CardResponse? Card { get; set; }
}

internal sealed class SellerReceivableBreakdown
{
    public Money? GrossAmount { get; set; }
    public Money? PaypalFee { get; set; }
    public Money? NetAmount { get; set; }
}

internal sealed class SellerPayableBreakdown
{
    public Money? GrossAmount { get; set; }
    public Money? PaypalFee { get; set; }
    public Money? NetAmount { get; set; }
    public Money? TotalRefundedAmount { get; set; }
}

internal sealed class AuthorizationResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public Money? Amount { get; set; }
    public string? ExpirationTime { get; set; }
    public List<LinkDescription>? Links { get; set; }
}

internal sealed class CaptureResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public Money? Amount { get; set; }
    public bool? FinalCapture { get; set; }
    public SellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal sealed class RefundResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public Money? Amount { get; set; }
    public SellerPayableBreakdown? SellerPayableBreakdown { get; set; }
}

internal sealed class PaymentCollection
{
    public List<AuthorizationResponse>? Authorizations { get; set; }
    public List<CaptureResponse>? Captures { get; set; }
}

internal sealed class PurchaseUnitResponse
{
    public PaymentCollection? Payments { get; set; }
}

internal sealed class OrderResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PaymentSourceResponse? PaymentSource { get; set; }
    public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }
    public List<LinkDescription>? Links { get; set; }
}

internal sealed class CustomerResponse
{
    public string? Id { get; set; }
}

internal sealed class PaymentTokenResponse
{
    public string? Id { get; set; }
    public CustomerResponse? Customer { get; set; }
    public PaymentSourceResponse? PaymentSource { get; set; }
}

// --- Transaction search ---

internal sealed class TransactionInfo
{
    public string? TransactionId { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public Money? TransactionAmount { get; set; }
    public Money? FeeAmount { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? TransactionInitiationDate { get; set; }
}

internal sealed class TransactionDetail
{
    public TransactionInfo? TransactionInfo { get; set; }
}

internal sealed class TransactionSearchResponse
{
    public List<TransactionDetail>? TransactionDetails { get; set; }
    public int? Page { get; set; }
    public int? TotalItems { get; set; }
    public int? TotalPages { get; set; }
}

// --- Error model ---

internal sealed class PayPalErrorDetail
{
    public string? Field { get; set; }
    public string? Value { get; set; }
    public string? Issue { get; set; }
    public string? Description { get; set; }
}

internal sealed class PayPalErrorResponse
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public List<PayPalErrorDetail>? Details { get; set; }
}
