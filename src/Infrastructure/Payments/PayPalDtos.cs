using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class PayPalMoneyDto
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

internal sealed class PayPalLinkDto
{
    [JsonPropertyName("href")]
    public string? Href { get; set; }

    [JsonPropertyName("rel")]
    public string? Rel { get; set; }

    [JsonPropertyName("method")]
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

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }
}

internal sealed class PayPalCardDto
{
    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("billing_address")]
    public PayPalAddressDto? BillingAddress { get; set; }

    [JsonPropertyName("vault_id")]
    public string? VaultId { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    [JsonPropertyName("experience_context")]
    public PayPalExperienceContextDto? ExperienceContext { get; set; }
}

internal sealed class PayPalExperienceContextDto
{
    [JsonPropertyName("brand_name")]
    public string? BrandName { get; set; }

    [JsonPropertyName("locale")]
    public string? Locale { get; set; }

    [JsonPropertyName("return_url")]
    public string? ReturnUrl { get; set; }

    [JsonPropertyName("cancel_url")]
    public string? CancelUrl { get; set; }
}

internal sealed class PayPalPaymentSourceDto
{
    [JsonPropertyName("card")]
    public PayPalCardDto? Card { get; set; }

    [JsonPropertyName("token")]
    public PayPalTokenDto? Token { get; set; }
}

internal sealed class PayPalTokenDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

internal sealed class PayPalPurchaseUnitDto
{
    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoneyDto? Amount { get; set; }

    [JsonPropertyName("payments")]
    public PayPalPaymentsDto? Payments { get; set; }
}

internal sealed class PayPalPaymentsDto
{
    [JsonPropertyName("authorizations")]
    public List<PayPalAuthorizationDto>? Authorizations { get; set; }

    [JsonPropertyName("captures")]
    public List<PayPalCaptureDto>? Captures { get; set; }
}

internal sealed class PayPalCreateOrderRequest
{
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "AUTHORIZE";

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnitDto> PurchaseUnits { get; set; } = new();

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalAuthorizeRequest
{
    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalOrderDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("intent")]
    public string? Intent { get; set; }

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnitDto>? PurchaseUnits { get; set; }

    [JsonPropertyName("links")]
    public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class PayPalAuthorizationDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoneyDto? Amount { get; set; }

    [JsonPropertyName("expiration_time")]
    public string? ExpirationTime { get; set; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }
}

internal sealed class PayPalCaptureDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoneyDto? Amount { get; set; }

    [JsonPropertyName("final_capture")]
    public bool? FinalCapture { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public PayPalSellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }
}

internal sealed class PayPalSellerReceivableBreakdownDto
{
    [JsonPropertyName("gross_amount")]
    public PayPalMoneyDto? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public PayPalMoneyDto? PaypalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public PayPalMoneyDto? NetAmount { get; set; }
}

internal sealed class PayPalCaptureRequest
{
    [JsonPropertyName("amount")]
    public PayPalMoneyDto? Amount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("final_capture")]
    public bool FinalCapture { get; set; } = true;
}

internal sealed class PayPalReauthorizeRequest
{
    [JsonPropertyName("amount")]
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalRefundRequest
{
    [JsonPropertyName("amount")]
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalRefundDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoneyDto? Amount { get; set; }

    [JsonPropertyName("seller_payable_breakdown")]
    public PayPalSellerPayableBreakdownDto? SellerPayableBreakdown { get; set; }
}

internal sealed class PayPalSellerPayableBreakdownDto
{
    [JsonPropertyName("gross_amount")]
    public PayPalMoneyDto? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public PayPalMoneyDto? PaypalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public PayPalMoneyDto? NetAmount { get; set; }

    [JsonPropertyName("total_refunded_amount")]
    public PayPalMoneyDto? TotalRefundedAmount { get; set; }
}

internal sealed class PayPalSetupTokenRequest
{
    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceDto? PaymentSource { get; set; }

    [JsonPropertyName("customer")]
    public PayPalCustomerDto? Customer { get; set; }
}

internal sealed class PayPalCustomerDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

internal sealed class PayPalSetupTokenDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("customer")]
    public PayPalCustomerDto? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceDto? PaymentSource { get; set; }

    [JsonPropertyName("links")]
    public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class PayPalPaymentTokenRequest
{
    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalPaymentTokenDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("customer")]
    public PayPalCustomerDto? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalOAuthTokenDto
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal sealed class PayPalErrorDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("debug_id")]
    public string? DebugId { get; set; }

    [JsonPropertyName("details")]
    public List<PayPalErrorDetailDto>? Details { get; set; }
}

internal sealed class PayPalErrorDetailDto
{
    [JsonPropertyName("issue")]
    public string? Issue { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }
}

internal sealed class PayPalTransactionSearchDto
{
    [JsonPropertyName("transaction_details")]
    public List<PayPalTransactionDetailDto>? TransactionDetails { get; set; }

    [JsonPropertyName("page")]
    public int? Page { get; set; }

    [JsonPropertyName("total_pages")]
    public int? TotalPages { get; set; }

    [JsonPropertyName("total_items")]
    public int? TotalItems { get; set; }
}

internal sealed class PayPalTransactionDetailDto
{
    [JsonPropertyName("transaction_info")]
    public PayPalTransactionInfoDto? TransactionInfo { get; set; }
}

internal sealed class PayPalTransactionInfoDto
{
    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("paypal_reference_id")]
    public string? PaypalReferenceId { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_field")]
    public string? CustomField { get; set; }

    [JsonPropertyName("transaction_event_code")]
    public string? TransactionEventCode { get; set; }

    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }

    [JsonPropertyName("transaction_amount")]
    public PayPalMoneyDto? TransactionAmount { get; set; }

    [JsonPropertyName("fee_amount")]
    public PayPalMoneyDto? FeeAmount { get; set; }

    [JsonPropertyName("transaction_initiation_date")]
    public string? TransactionInitiationDate { get; set; }
}
