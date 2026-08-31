using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Wire DTOs for the PayPal REST API. Property names are pinned explicitly so
// serialization never depends on naming-policy behavior.

internal class PayPalMoney
{
    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

internal class PayPalCardSource
{
    [JsonPropertyName("number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Number { get; set; }

    [JsonPropertyName("expiry")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Expiry { get; set; }

    [JsonPropertyName("security_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("billing_address")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PayPalAddress? BillingAddress { get; set; }

    [JsonPropertyName("vault_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VaultId { get; set; }

    // Response-only fields
    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }
}

internal class PayPalAddress
{
    [JsonPropertyName("address_line_1")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AddressLine1 { get; set; }

    [JsonPropertyName("admin_area_2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AdminArea2 { get; set; }

    [JsonPropertyName("admin_area_1")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AdminArea1 { get; set; }

    [JsonPropertyName("postal_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PostalCode { get; set; }

    [JsonPropertyName("country_code")]
    public string CountryCode { get; set; } = "US";
}

internal class PayPalTokenSource
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

internal class PayPalPaymentSource
{
    [JsonPropertyName("card")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PayPalCardSource? Card { get; set; }

    [JsonPropertyName("token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PayPalTokenSource? Token { get; set; }
}

internal class PayPalCustomer
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

internal class PayPalPurchaseUnitRequest
{
    [JsonPropertyName("amount")]
    public PayPalMoney Amount { get; set; } = new();

    [JsonPropertyName("invoice_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InvoiceId { get; set; }
}

internal class PayPalCreateOrderRequest
{
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "AUTHORIZE";

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnitRequest> PurchaseUnits { get; set; } = new();

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource PaymentSource { get; set; } = new();
}

internal class PayPalSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")]
    public PayPalMoney? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public PayPalMoney? PayPalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public PayPalMoney? NetAmount { get; set; }
}

internal class PayPalAuthorizationDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("expiration_time")]
    public string? ExpirationTime { get; set; }
}

internal class PayPalCaptureDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal class PayPalPaymentsDto
{
    [JsonPropertyName("authorizations")]
    public List<PayPalAuthorizationDto>? Authorizations { get; set; }

    [JsonPropertyName("captures")]
    public List<PayPalCaptureDto>? Captures { get; set; }
}

internal class PayPalPurchaseUnitResponse
{
    [JsonPropertyName("payments")]
    public PayPalPaymentsDto? Payments { get; set; }
}

internal class PayPalOrderResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnitResponse>? PurchaseUnits { get; set; }
}

internal class PayPalCaptureRequest
{
    [JsonPropertyName("final_capture")]
    public bool FinalCapture { get; set; } = true;
}

internal class PayPalReauthorizeRequest
{
    [JsonPropertyName("amount")]
    public PayPalMoney Amount { get; set; } = new();
}

internal class PayPalRefundRequest
{
    [JsonPropertyName("amount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PayPalMoney? Amount { get; set; }
}

internal class PayPalRefundDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }
}

internal class PayPalSetupTokenRequest
{
    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource PaymentSource { get; set; } = new();

    [JsonPropertyName("customer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PayPalCustomer? Customer { get; set; }
}

internal class PayPalSetupTokenResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("customer")]
    public PayPalCustomer? Customer { get; set; }
}

internal class PayPalPaymentTokenRequest
{
    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource PaymentSource { get; set; } = new();
}

internal class PayPalPaymentTokenResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("customer")]
    public PayPalCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource? PaymentSource { get; set; }
}

internal class PayPalTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal class PayPalErrorDetail
{
    [JsonPropertyName("issue")]
    public string? Issue { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal class PayPalErrorResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("debug_id")]
    public string? DebugId { get; set; }

    [JsonPropertyName("details")]
    public List<PayPalErrorDetail>? Details { get; set; }
}

internal class PayPalTransactionInfo
{
    [JsonPropertyName("transaction_id")]
    public string TransactionId { get; set; } = string.Empty;

    [JsonPropertyName("transaction_event_code")]
    public string? TransactionEventCode { get; set; }

    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }

    [JsonPropertyName("transaction_amount")]
    public PayPalMoney? TransactionAmount { get; set; }

    [JsonPropertyName("fee_amount")]
    public PayPalMoney? FeeAmount { get; set; }

    [JsonPropertyName("transaction_initiation_date")]
    public string? TransactionInitiationDate { get; set; }

    [JsonPropertyName("transaction_updated_date")]
    public string? TransactionUpdatedDate { get; set; }
}

internal class PayPalTransactionDetail
{
    [JsonPropertyName("transaction_info")]
    public PayPalTransactionInfo? TransactionInfo { get; set; }
}

internal class PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")]
    public List<PayPalTransactionDetail>? TransactionDetails { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }
}
