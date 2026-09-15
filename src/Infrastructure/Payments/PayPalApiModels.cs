using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

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

internal sealed class PayPalAmountBreakdownDto
{
    public PayPalMoneyDto? ItemTotal { get; set; }
}

internal sealed class PayPalItemDto
{
    public string Name { get; set; } = string.Empty;
    public string Quantity { get; set; } = string.Empty;
    public PayPalMoneyDto UnitAmount { get; set; } = new();
}

internal sealed class PayPalPurchaseUnitRequestDto
{
    public string? CustomId { get; set; }
    public string? InvoiceId { get; set; }
    public PayPalAmountRequestDto Amount { get; set; } = new();
    public List<PayPalItemDto>? Items { get; set; }
}

internal sealed class PayPalAmountRequestDto
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public PayPalAmountBreakdownDto? Breakdown { get; set; }
}

internal sealed class PayPalCreateOrderRequestDto
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PayPalPurchaseUnitRequestDto> PurchaseUnits { get; set; } = new();
}

internal sealed class PayPalBillingAddressDto
{
    public string CountryCode { get; set; } = string.Empty;

    // JsonNamingPolicy.SnakeCaseLower emits address_line1; PayPal requires address_line_1.
    [JsonPropertyName("address_line_1")]
    public string? AddressLine1 { get; set; }

    [JsonPropertyName("address_line_2")]
    public string? AddressLine2 { get; set; }

    [JsonPropertyName("admin_area_2")]
    public string? AdminArea2 { get; set; }

    [JsonPropertyName("admin_area_1")]
    public string? AdminArea1 { get; set; }

    public string? PostalCode { get; set; }
}

internal sealed class PayPalCardVerificationDto
{
    public string Method { get; set; } = "AVS_CVV";
}

internal sealed class PayPalCardAttributesDto
{
    public PayPalCardVerificationDto? Verification { get; set; }
}

internal sealed class PayPalStoredCredentialDto
{
    public string PaymentInitiator { get; set; } = string.Empty;
    public string PaymentType { get; set; } = string.Empty;
    public string? Usage { get; set; }
}

internal sealed class PayPalCardRequestDto
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public PayPalBillingAddressDto? BillingAddress { get; set; }
    public PayPalCardAttributesDto? Attributes { get; set; }
    public string? VaultId { get; set; }
    public PayPalStoredCredentialDto? StoredCredential { get; set; }
}

internal sealed class PayPalPaymentSourceDto
{
    public PayPalCardRequestDto? Card { get; set; }
}

internal sealed class PayPalAuthorizeRequestDto
{
    public PayPalPaymentSourceDto PaymentSource { get; set; } = new();
}

internal sealed class PayPalSellerReceivableBreakdownDto
{
    public PayPalMoneyDto? GrossAmount { get; set; }
    public PayPalMoneyDto? PaypalFee { get; set; }
    public PayPalMoneyDto? NetAmount { get; set; }
}

internal sealed class PayPalAuthorizationResourceDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
    public string? ExpirationTime { get; set; }
    public string? CreateTime { get; set; }
}

internal sealed class PayPalCaptureResourceDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
    public PayPalSellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
}

internal sealed class PayPalPaymentsDto
{
    public List<PayPalAuthorizationResourceDto>? Authorizations { get; set; }
    public List<PayPalCaptureResourceDto>? Captures { get; set; }
}

internal sealed class PayPalPurchaseUnitDto
{
    public PayPalPaymentsDto? Payments { get; set; }
}

internal sealed class PayPalOrderResourceDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public List<PayPalPurchaseUnitDto>? PurchaseUnits { get; set; }
    public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class PayPalCaptureRequestDto
{
    public PayPalMoneyDto? Amount { get; set; }
    public bool FinalCapture { get; set; } = true;
    public string? InvoiceId { get; set; }
}

internal sealed class PayPalReauthorizeRequestDto
{
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalRefundRequestDto
{
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalRefundResourceDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalVaultCardDto
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public PayPalBillingAddressDto? BillingAddress { get; set; }
}

internal sealed class PayPalVaultPaymentSourceDto
{
    public PayPalVaultCardDto? Card { get; set; }
}

internal sealed class PayPalVaultCustomerDto
{
    public string? Id { get; set; }
    public string? MerchantCustomerId { get; set; }
}

internal sealed class PayPalCreatePaymentTokenRequestDto
{
    public PayPalVaultPaymentSourceDto PaymentSource { get; set; } = new();
    public PayPalVaultCustomerDto? Customer { get; set; }
}

internal sealed class PayPalVaultedCardResponseDto
{
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? Name { get; set; }
}

internal sealed class PayPalVaultPaymentSourceResponseDto
{
    public PayPalVaultedCardResponseDto? Card { get; set; }
}

internal sealed class PayPalPaymentTokenResponseDto
{
    public string? Id { get; set; }
    public PayPalVaultCustomerDto? Customer { get; set; }
    public PayPalVaultPaymentSourceResponseDto? PaymentSource { get; set; }
}

internal sealed class PayPalErrorDetailDto
{
    public string? Field { get; set; }
    public string? Issue { get; set; }
    public string? Description { get; set; }
}

internal sealed class PayPalErrorResponseDto
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public List<PayPalErrorDetailDto>? Details { get; set; }
}

internal sealed class PayPalTokenResponseDto
{
    public string? AccessToken { get; set; }
    public string? TokenType { get; set; }
    public int ExpiresIn { get; set; }
}

internal sealed class PayPalTransactionInfoDto
{
    public string? TransactionId { get; set; }
    public string? PaypalReferenceId { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionInitiationDate { get; set; }
    public string? TransactionStatus { get; set; }
    public PayPalMoneyDto? TransactionAmount { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
}

internal sealed class PayPalTransactionDetailDto
{
    public PayPalTransactionInfoDto? TransactionInfo { get; set; }
}

internal sealed class PayPalTransactionSearchResponseDto
{
    public List<PayPalTransactionDetailDto>? TransactionDetails { get; set; }
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public int Page { get; set; }
}
