using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class PayPalAccessTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal sealed class PayPalErrorBody
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public List<PayPalErrorDetail>? Details { get; set; }
}

internal sealed class PayPalErrorDetail
{
    public string? Field { get; set; }
    public string? Value { get; set; }
    public string? Location { get; set; }
    public string? Issue { get; set; }
    public string? Description { get; set; }
}

internal sealed class PayPalMoneyDto
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal sealed class PayPalAmountDto
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public PayPalAmountBreakdownDto? Breakdown { get; set; }
}

internal sealed class PayPalAmountBreakdownDto
{
    public PayPalMoneyDto? ItemTotal { get; set; }
}

internal sealed class PayPalNameDto
{
    public string? GivenName { get; set; }
    public string? Surname { get; set; }
}

internal sealed class PayPalAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

internal sealed class PayPalCardRequestDto
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public PayPalAddressDto? BillingAddress { get; set; }
    public string? VaultId { get; set; }
    public PayPalCardAttributesDto? Attributes { get; set; }
    public PayPalStoredCredentialDto? StoredCredential { get; set; }
}

internal sealed class PayPalCardAttributesDto
{
    public PayPalCardVerificationDto? Verification { get; set; }
}

internal sealed class PayPalCardVerificationDto
{
    public string? Method { get; set; }
}

internal sealed class PayPalStoredCredentialDto
{
    public string PaymentInitiator { get; set; } = string.Empty;
    public string PaymentType { get; set; } = string.Empty;
    public string? Usage { get; set; }
}

internal sealed class PayPalPaymentSourceDto
{
    public PayPalCardRequestDto? Card { get; set; }
}

internal sealed class PayPalPurchaseUnitRequestDto
{
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public PayPalAmountDto? Amount { get; set; }
    public List<PayPalItemDto>? Items { get; set; }
}

internal sealed class PayPalItemDto
{
    public string Name { get; set; } = string.Empty;
    public string Quantity { get; set; } = string.Empty;
    public PayPalMoneyDto? UnitAmount { get; set; }
}

internal sealed class PayPalCreateOrderRequestDto
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PayPalPurchaseUnitRequestDto> PurchaseUnits { get; set; } = new();
    public PayPalPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalAuthorizeRequestDto
{
    public PayPalPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalLinkDto
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}

internal sealed class PayPalOrderResponseDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public List<PayPalPurchaseUnitResponseDto>? PurchaseUnits { get; set; }
    public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class PayPalPurchaseUnitResponseDto
{
    public PayPalPaymentCollectionDto? Payments { get; set; }
    public PayPalAmountDto? Amount { get; set; }
}

internal sealed class PayPalPaymentCollectionDto
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
    public string? UpdateTime { get; set; }
}

internal sealed class PayPalSellerReceivableBreakdownDto
{
    public PayPalMoneyDto? GrossAmount { get; set; }
    public PayPalMoneyDto? PaypalFee { get; set; }
    public PayPalMoneyDto? NetAmount { get; set; }
}

internal sealed class PayPalCaptureDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
    public PayPalSellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
    public bool FinalCapture { get; set; }
}

internal sealed class PayPalCaptureRequestDto
{
    public PayPalMoneyDto? Amount { get; set; }
    public bool FinalCapture { get; set; } = true;
}

internal sealed class PayPalReauthorizeRequestDto
{
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalRefundRequestDto
{
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalRefundDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalCustomerDto
{
    public string? Id { get; set; }
    public string? MerchantCustomerId { get; set; }
}

internal sealed class PayPalVaultCardDto
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public PayPalAddressDto? BillingAddress { get; set; }
}

internal sealed class PayPalVaultPaymentSourceDto
{
    public PayPalVaultCardDto? Card { get; set; }
}

internal sealed class PayPalVaultRequestDto
{
    public PayPalCustomerDto? Customer { get; set; }
    public PayPalVaultPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalVaultCardResponseDto
{
    public string? Name { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

internal sealed class PayPalVaultPaymentSourceResponseDto
{
    public PayPalVaultCardResponseDto? Card { get; set; }
}

internal sealed class PayPalVaultResponseDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalCustomerDto? Customer { get; set; }
    public PayPalVaultPaymentSourceResponseDto? PaymentSource { get; set; }
    public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class PayPalTransactionSearchResponseDto
{
    public List<PayPalTransactionDetailDto>? TransactionDetails { get; set; }
    public int Page { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

internal sealed class PayPalTransactionDetailDto
{
    public PayPalTransactionInfoDto? TransactionInfo { get; set; }
}

internal sealed class PayPalTransactionInfoDto
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
}
