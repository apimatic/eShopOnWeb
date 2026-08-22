using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

internal sealed class PayPalErrorBody
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public List<PayPalErrorDetail>? Details { get; set; }

    public string ToPublicMessage()
    {
        if (Details is { Count: > 0 })
        {
            var parts = new List<string>();
            foreach (var detail in Details)
            {
                var issue = string.IsNullOrWhiteSpace(detail.Issue) ? Name : detail.Issue;
                var location = string.IsNullOrWhiteSpace(detail.Field) ? string.Empty : $" ({detail.Field})";
                var description = detail.Description ?? Message;
                parts.Add($"{issue}{location}: {description}".Trim());
            }
            return string.Join("; ", parts);
        }

        if (!string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Message))
        {
            return $"{Name}: {Message}";
        }

        return Message ?? Name ?? "PayPal request failed.";
    }
}

internal sealed class PayPalErrorDetail
{
    public string? Issue { get; set; }
    public string? Description { get; set; }
    public string? Field { get; set; }
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

internal sealed class PayPalMoneyDto
{
    public string? CurrencyCode { get; set; }
    public string? Value { get; set; }
}

internal sealed class PayPalLinkDto
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}

internal sealed class PayPalAddressDto
{
    [JsonPropertyName("address_line_1")]
    public string? AddressLine1 { get; set; }

    [JsonPropertyName("address_line_2")]
    public string? AddressLine2 { get; set; }

    [JsonPropertyName("admin_area_2")]
    public string? AdminArea2 { get; set; }

    [JsonPropertyName("admin_area_1")]
    public string? AdminArea1 { get; set; }

    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

internal sealed class PayPalCardStoredCredentialDto
{
    public string? PaymentInitiator { get; set; }
    public string? PaymentType { get; set; }
    public string? Usage { get; set; }
}

internal sealed class PayPalCardVerificationDto
{
    public string? Method { get; set; }
}

internal sealed class PayPalCardAttributesDto
{
    public PayPalCardVerificationDto? Verification { get; set; }
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
    public PayPalCardStoredCredentialDto? StoredCredential { get; set; }
}

internal sealed class PayPalPaymentSourceDto
{
    public PayPalCardRequestDto? Card { get; set; }
}

internal sealed class PayPalAmountRequestDto
{
    public string? CurrencyCode { get; set; }
    public string? Value { get; set; }
}

internal sealed class PayPalPurchaseUnitRequestDto
{
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public PayPalAmountRequestDto? Amount { get; set; }
}

internal sealed class PayPalCreateOrderRequestDto
{
    public string? Intent { get; set; }
    public List<PayPalPurchaseUnitRequestDto>? PurchaseUnits { get; set; }
    public PayPalPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalSellerReceivableBreakdownDto
{
    public PayPalMoneyDto? GrossAmount { get; set; }
    public PayPalMoneyDto? PaypalFee { get; set; }
    public PayPalMoneyDto? NetAmount { get; set; }
}

internal sealed class PayPalAuthorizationDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
    public string? CreateTime { get; set; }
    public string? ExpirationTime { get; set; }
    public string? UpdateTime { get; set; }
}

internal sealed class PayPalCaptureDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
    public PayPalSellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
    public string? CreateTime { get; set; }
    public bool FinalCapture { get; set; }
}

internal sealed class PayPalRefundDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
    public string? CreateTime { get; set; }
}

internal sealed class PayPalPaymentCollectionDto
{
    public List<PayPalAuthorizationDto>? Authorizations { get; set; }
    public List<PayPalCaptureDto>? Captures { get; set; }
    public List<PayPalRefundDto>? Refunds { get; set; }
}

internal sealed class PayPalPurchaseUnitDto
{
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public PayPalPaymentCollectionDto? Payments { get; set; }
}

internal sealed class PayPalOrderDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public string? Intent { get; set; }
    public List<PayPalPurchaseUnitDto>? PurchaseUnits { get; set; }
    public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class PayPalCaptureRequestDto
{
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

internal sealed class PayPalVaultCustomerDto
{
    public string? Id { get; set; }
    public string? MerchantCustomerId { get; set; }
}

internal sealed class PayPalVaultCardResponseDto
{
    public string? Name { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? Type { get; set; }
}

internal sealed class PayPalVaultPaymentSourceResponseDto
{
    public PayPalVaultCardResponseDto? Card { get; set; }
}

internal sealed class PayPalCreatePaymentTokenRequestDto
{
    public PayPalVaultCustomerDto? Customer { get; set; }
    public PayPalPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalPaymentTokenResponseDto
{
    public string? Id { get; set; }
    public PayPalVaultCustomerDto? Customer { get; set; }
    public PayPalVaultPaymentSourceResponseDto? PaymentSource { get; set; }
}

internal sealed class PayPalTransactionAmountDto
{
    public string? CurrencyCode { get; set; }
    public string? Value { get; set; }
}

internal sealed class PayPalTransactionInfoDto
{
    public string? TransactionId { get; set; }
    public string? PaypalReferenceId { get; set; }
    public string? PaypalReferenceIdType { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? TransactionInitiationDate { get; set; }
    public string? TransactionUpdatedDate { get; set; }
    public PayPalTransactionAmountDto? TransactionAmount { get; set; }
    public PayPalTransactionAmountDto? FeeAmount { get; set; }
}

internal sealed class PayPalTransactionDetailDto
{
    public PayPalTransactionInfoDto? TransactionInfo { get; set; }
}

internal sealed class PayPalSearchResponseDto
{
    public List<PayPalTransactionDetailDto>? TransactionDetails { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    public int Page { get; set; }
}
