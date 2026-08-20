using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

internal sealed class PayPalTokenResponse
{
    public string? AccessToken { get; set; }
    public int ExpiresIn { get; set; }
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

internal sealed class PayPalOrderRequest
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PayPalPurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    public PayPalPaymentSourceRequest? PaymentSource { get; set; }
}

internal sealed class PayPalPurchaseUnitRequest
{
    public string? ReferenceId { get; set; }
    public string? CustomId { get; set; }
    public string? InvoiceId { get; set; }
    public string? Description { get; set; }
    public PayPalAmountRequest Amount { get; set; } = new();
    public List<PayPalItemRequest>? Items { get; set; }
}

internal sealed class PayPalAmountRequest
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public PayPalAmountBreakdown? Breakdown { get; set; }
}

internal sealed class PayPalAmountBreakdown
{
    public PayPalMoneyDto? ItemTotal { get; set; }
}

internal sealed class PayPalItemRequest
{
    public string Name { get; set; } = string.Empty;
    public string Quantity { get; set; } = string.Empty;
    public PayPalMoneyDto UnitAmount { get; set; } = new();
    public string? Sku { get; set; }
    public string Category { get; set; } = "PHYSICAL_GOODS";
}

internal sealed class PayPalPaymentSourceRequest
{
    public PayPalCardRequest? Card { get; set; }
}

internal sealed class PayPalCardRequest
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? VaultId { get; set; }
    public PayPalCardBillingAddress? BillingAddress { get; set; }
    public PayPalCardAttributes? Attributes { get; set; }
    public PayPalStoredCredential? StoredCredential { get; set; }
}

internal sealed class PayPalCardBillingAddress
{
    public string CountryCode { get; set; } = string.Empty;

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

internal sealed class PayPalCardAttributes
{
    public PayPalCardVerification? Verification { get; set; }
}

internal sealed class PayPalCardVerification
{
    public string Method { get; set; } = "AVS_CVV";
}

internal sealed class PayPalStoredCredential
{
    public string PaymentInitiator { get; set; } = "CUSTOMER";
    public string PaymentType { get; set; } = "UNSCHEDULED";
    public string Usage { get; set; } = "SUBSEQUENT";
}

internal sealed class PayPalOrderResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public List<PayPalLinkDto>? Links { get; set; }
    public List<PayPalPurchaseUnitResponse>? PurchaseUnits { get; set; }
}

internal sealed class PayPalPurchaseUnitResponse
{
    public PayPalPaymentCollection? Payments { get; set; }
}

internal sealed class PayPalPaymentCollection
{
    public List<PayPalAuthorizationResource>? Authorizations { get; set; }
    public List<PayPalCaptureResource>? Captures { get; set; }
}

internal sealed class PayPalAuthorizationResource
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
    public string? ExpirationTime { get; set; }
    public string? CreateTime { get; set; }
}

internal sealed class PayPalCaptureResource
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
}

internal sealed class PayPalReauthorizeRequest
{
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalRefundRequest
{
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalRefundResource
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalVaultRequest
{
    public PayPalVaultCustomer? Customer { get; set; }
    public PayPalVaultPaymentSource? PaymentSource { get; set; }
}

internal sealed class PayPalVaultCustomer
{
    public string? MerchantCustomerId { get; set; }
}

internal sealed class PayPalVaultPaymentSource
{
    public PayPalCardRequest? Card { get; set; }
}

internal sealed class PayPalVaultResponse
{
    public string? Id { get; set; }
    public PayPalVaultPaymentSourceResponse? PaymentSource { get; set; }
}

internal sealed class PayPalVaultPaymentSourceResponse
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

internal sealed class PayPalTransactionSearchResponse
{
    public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
    public int? TotalPages { get; set; }
    public int? TotalItems { get; set; }
    public int? Page { get; set; }
}

internal sealed class PayPalTransactionDetail
{
    public PayPalTransactionInfo? TransactionInfo { get; set; }
}

internal sealed class PayPalTransactionInfo
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

internal static class PayPalJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
