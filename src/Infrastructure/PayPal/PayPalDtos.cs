using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

internal sealed class PayPalTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }
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
    public string? Issue { get; set; }
    public string? Description { get; set; }
    public string? Field { get; set; }
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

internal sealed class PayPalOrderDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public List<PayPalPurchaseUnitDto>? PurchaseUnits { get; set; }
    public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class PayPalPurchaseUnitDto
{
    public string? CustomId { get; set; }
    public string? InvoiceId { get; set; }
    public PayPalPaymentCollectionDto? Payments { get; set; }
}

internal sealed class PayPalPaymentCollectionDto
{
    public List<PayPalAuthorizationDto>? Authorizations { get; set; }
    public List<PayPalCaptureDto>? Captures { get; set; }
}

internal sealed class PayPalAuthorizationDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
    public string? ExpirationTime { get; set; }
}

internal sealed class PayPalCaptureDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
    public PayPalSellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
}

internal sealed class PayPalSellerReceivableBreakdownDto
{
    public PayPalMoneyDto? GrossAmount { get; set; }
    public PayPalMoneyDto? PaypalFee { get; set; }
    public PayPalMoneyDto? NetAmount { get; set; }
}

internal sealed class PayPalRefundDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalPaymentTokenDto
{
    public string? Id { get; set; }
    public PayPalCustomerDto? Customer { get; set; }
    public PayPalPaymentSourceResponseDto? PaymentSource { get; set; }
}

internal sealed class PayPalCustomerDto
{
    public string? Id { get; set; }
    public string? MerchantCustomerId { get; set; }
}

internal sealed class PayPalPaymentSourceResponseDto
{
    public PayPalCardResponseDto? Card { get; set; }
}

internal sealed class PayPalCardResponseDto
{
    public string? Name { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

internal sealed class PayPalTransactionSearchDto
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
    public string? TransactionEventCode { get; set; }
    public string? TransactionInitiationDate { get; set; }
    public PayPalMoneyDto? TransactionAmount { get; set; }
    public PayPalMoneyDto? FeeAmount { get; set; }
    public string? TransactionStatus { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
}
