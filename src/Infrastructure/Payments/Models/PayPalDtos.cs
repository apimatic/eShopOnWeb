using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Models;

internal sealed class PayPalMoneyDto
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal sealed class PayPalLinkDto
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}

internal sealed class PayPalErrorDto
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public List<PayPalErrorDetailDto>? Details { get; set; }
}

internal sealed class PayPalErrorDetailDto
{
    public string? Field { get; set; }
    public string? Value { get; set; }
    public string? Location { get; set; }
    public string? Issue { get; set; }
    public string? Description { get; set; }
}

internal sealed class PayPalTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal sealed class PayPalOrderRequest
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PayPalPurchaseUnitRequest> PurchaseUnits { get; set; } = new();
}

internal sealed class PayPalPurchaseUnitRequest
{
    public PayPalAmountWithBreakdown Amount { get; set; } = new();
    public string? CustomId { get; set; }
    public string? InvoiceId { get; set; }
    public string? Description { get; set; }
}

internal sealed class PayPalAmountWithBreakdown
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal sealed class PayPalAuthorizeRequest
{
    public PayPalPaymentSource? PaymentSource { get; set; }
}

internal sealed class PayPalPaymentSource
{
    public PayPalCardRequest? Card { get; set; }
}

internal sealed class PayPalCardRequest
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public PayPalAddressDto? BillingAddress { get; set; }
    public string? VaultId { get; set; }
    public PayPalCardStoredCredential? StoredCredential { get; set; }
}

internal sealed class PayPalAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}

internal sealed class PayPalCardStoredCredential
{
    public string PaymentInitiator { get; set; } = string.Empty;
    public string PaymentType { get; set; } = string.Empty;
    public string? Usage { get; set; }
}

internal sealed class PayPalOrderDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public string? Intent { get; set; }
    public List<PayPalPurchaseUnitDto>? PurchaseUnits { get; set; }
    public PayPalPaymentSourceResponse? PaymentSource { get; set; }
    public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class PayPalPaymentSourceResponse
{
    public PayPalCardResponse? Card { get; set; }
}

internal sealed class PayPalCardResponse
{
    public string? Name { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Type { get; set; }
    public string? Expiry { get; set; }
}

internal sealed class PayPalPurchaseUnitDto
{
    public string? ReferenceId { get; set; }
    public string? CustomId { get; set; }
    public string? InvoiceId { get; set; }
    public PayPalPaymentCollection? Payments { get; set; }
}

internal sealed class PayPalPaymentCollection
{
    public List<PayPalAuthorizationDto>? Authorizations { get; set; }
    public List<PayPalCaptureDto>? Captures { get; set; }
    public List<PayPalRefundDto>? Refunds { get; set; }
}

internal sealed class PayPalAuthorizationDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
    public string? ExpirationTime { get; set; }
    public string? CreateTime { get; set; }
}

internal sealed class PayPalCaptureDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
    public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal sealed class PayPalSellerReceivableBreakdown
{
    public PayPalMoneyDto? GrossAmount { get; set; }
    public PayPalMoneyDto? PaypalFee { get; set; }
    public PayPalMoneyDto? NetAmount { get; set; }
}

internal sealed class PayPalCaptureRequest
{
    public PayPalMoneyDto? Amount { get; set; }
    public bool FinalCapture { get; set; } = true;
    public string? InvoiceId { get; set; }
}

internal sealed class PayPalReauthorizeRequest
{
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalRefundRequest
{
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalRefundDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalSetupTokenRequest
{
    public PayPalVaultCustomer? Customer { get; set; }
    public PayPalVaultPaymentSource PaymentSource { get; set; } = new();
}

internal sealed class PayPalPaymentTokenRequest
{
    public PayPalVaultCustomer? Customer { get; set; }
    public PayPalPaymentTokenSource PaymentSource { get; set; } = new();
}

internal sealed class PayPalVaultCustomer
{
    public string? Id { get; set; }
    public string? MerchantCustomerId { get; set; }
}

internal sealed class PayPalVaultPaymentSource
{
    public PayPalCardRequest? Card { get; set; }
}

internal sealed class PayPalPaymentTokenSource
{
    public PayPalVaultTokenRequest? Token { get; set; }
    public PayPalCardRequest? Card { get; set; }
}

internal sealed class PayPalVaultTokenRequest
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "SETUP_TOKEN";
}

internal sealed class PayPalSetupTokenResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalVaultCustomer? Customer { get; set; }
    public PayPalVaultedPaymentSource? PaymentSource { get; set; }
    public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class PayPalPaymentTokenResponse
{
    public string? Id { get; set; }
    public PayPalVaultCustomer? Customer { get; set; }
    public PayPalVaultedPaymentSource? PaymentSource { get; set; }
}

internal sealed class PayPalVaultedPaymentSource
{
    public PayPalVaultCardResponse? Card { get; set; }
}

internal sealed class PayPalVaultCardResponse
{
    public string? Name { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

internal sealed class PayPalSearchResponse
{
    public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
    public int? Page { get; set; }
    public int? TotalItems { get; set; }
    public int? TotalPages { get; set; }
}

internal sealed class PayPalTransactionDetail
{
    public PayPalTransactionInfo? TransactionInfo { get; set; }
}

internal sealed class PayPalTransactionInfo
{
    public string? TransactionId { get; set; }
    public string? PaypalReferenceId { get; set; }
    public string? PaypalReferenceIdType { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionInitiationDate { get; set; }
    public PayPalMoneyDto? TransactionAmount { get; set; }
    public PayPalMoneyDto? FeeAmount { get; set; }
    public string? TransactionStatus { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? InstrumentType { get; set; }
}
