using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Internal wire models for the PayPal REST API. Serialized with a snake_case naming policy;
// the numbered address fields carry explicit names because snake_case would drop the underscore.

internal sealed class PayPalTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
}

internal sealed class Money
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal sealed class CardBillingAddressModel
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

internal sealed class CardModel
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? VaultId { get; set; }
    public CardBillingAddressModel? BillingAddress { get; set; }

    // Present on responses only (safe display details).
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
}

internal sealed class PaymentSourceModel
{
    public CardModel? Card { get; set; }
}

internal sealed class PurchaseUnitRequest
{
    public Money Amount { get; set; } = new();
    public string? CustomId { get; set; }
    public string? InvoiceId { get; set; }
}

internal sealed class CreateOrderRequest
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    public PaymentSourceModel? PaymentSource { get; set; }
}

internal sealed class LinkModel
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}

internal sealed class AuthorizationModel
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public Money? Amount { get; set; }
    public DateTimeOffset? ExpirationTime { get; set; }
}

internal sealed class PaymentsCollectionModel
{
    public List<AuthorizationModel>? Authorizations { get; set; }
}

internal sealed class PurchaseUnitResponse
{
    public PaymentsCollectionModel? Payments { get; set; }
}

internal sealed class CreateOrderResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PaymentSourceModel? PaymentSource { get; set; }
    public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }
    public List<LinkModel>? Links { get; set; }
}

internal sealed class CaptureRequest
{
    public Money? Amount { get; set; }
    public bool FinalCapture { get; set; }
}

internal sealed class SellerReceivableBreakdown
{
    public Money? GrossAmount { get; set; }
    public Money? PaypalFee { get; set; }
    public Money? NetAmount { get; set; }
}

internal sealed class CaptureResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public Money? Amount { get; set; }
    public SellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal sealed class AmountOnlyRequest
{
    public Money? Amount { get; set; }
}

internal sealed class ReauthorizeResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public Money? Amount { get; set; }
    public DateTimeOffset? ExpirationTime { get; set; }
}

internal sealed class VoidResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
}

internal sealed class RefundRequest
{
    public Money? Amount { get; set; }
    public string? CustomId { get; set; }
}

internal sealed class RefundResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
}

// --- Vault (Payment Method Tokens v3) ---

internal sealed class VaultTokenRequest
{
    public PaymentSourceModel PaymentSource { get; set; } = new();
}

internal sealed class VaultCustomer
{
    public string? Id { get; set; }
}

internal sealed class VaultTokenResponse
{
    public string? Id { get; set; }
    public VaultCustomer? Customer { get; set; }
    public PaymentSourceModel? PaymentSource { get; set; }
}

// --- Transaction reporting (v1) ---

internal sealed class ReportTransactionInfo
{
    public string? TransactionId { get; set; }
    public string? PaypalReferenceId { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public DateTimeOffset? TransactionInitiationDate { get; set; }
    public Money? TransactionAmount { get; set; }
    public Money? FeeAmount { get; set; }
    public string? CustomField { get; set; }
    public string? InvoiceId { get; set; }
}

internal sealed class ReportTransactionDetail
{
    public ReportTransactionInfo? TransactionInfo { get; set; }
}

internal sealed class TransactionSearchResponse
{
    public List<ReportTransactionDetail>? TransactionDetails { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
}

// --- Error envelope ---

internal sealed class PayPalErrorDetail
{
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
