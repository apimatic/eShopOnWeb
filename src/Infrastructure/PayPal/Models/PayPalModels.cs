using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Models;

internal sealed class PayPalMoney
{
    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

internal sealed class PayPalError
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

internal sealed class PayPalOAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal sealed class PayPalAddress
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
    public string CountryCode { get; set; } = string.Empty;
}

internal sealed class PayPalCardRequest
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("billing_address")]
    public PayPalAddress? BillingAddress { get; set; }

    [JsonPropertyName("vault_id")]
    public string? VaultId { get; set; }

    [JsonPropertyName("stored_credential")]
    public PayPalStoredCredential? StoredCredential { get; set; }
}

internal sealed class PayPalStoredCredential
{
    [JsonPropertyName("payment_initiator")]
    public string PaymentInitiator { get; set; } = "CUSTOMER";

    [JsonPropertyName("payment_type")]
    public string PaymentType { get; set; } = "ONE_TIME";

    public string? Usage { get; set; }
}

internal sealed class PayPalPaymentSource
{
    public PayPalCardRequest? Card { get; set; }
}

internal sealed class PayPalPurchaseUnitRequest
{
    [JsonPropertyName("reference_id")]
    public string? ReferenceId { get; set; }

    public PayPalMoney Amount { get; set; } = new();

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }
}

internal sealed class PayPalOrderRequest
{
    public string Intent { get; set; } = "AUTHORIZE";

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnitRequest> PurchaseUnits { get; set; } = new();

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource? PaymentSource { get; set; }
}

internal sealed class PayPalOrderResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }

    public List<PayPalLink>? Links { get; set; }
}

internal sealed class PayPalPurchaseUnit
{
    public PayPalPaymentCollection? Payments { get; set; }
}

internal sealed class PayPalPaymentCollection
{
    public List<PayPalAuthorization>? Authorizations { get; set; }
    public List<PayPalCapture>? Captures { get; set; }
    public List<PayPalRefund>? Refunds { get; set; }
}

internal sealed class PayPalAuthorization
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("expiration_time")]
    public string? ExpirationTime { get; set; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }
}

internal sealed class PayPalCapture
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }
}

internal sealed class PayPalSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")]
    public PayPalMoney? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public PayPalMoney? PaypalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public PayPalMoney? NetAmount { get; set; }
}

internal sealed class PayPalRefund
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoney? Amount { get; set; }
}

internal sealed class PayPalCaptureRequest
{
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("final_capture")]
    public bool FinalCapture { get; set; } = true;
}

internal sealed class PayPalReauthorizeRequest
{
    public PayPalMoney? Amount { get; set; }
}

internal sealed class PayPalRefundRequest
{
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }
}

internal sealed class PayPalLink
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}

internal sealed class PayPalCustomer
{
    public string? Id { get; set; }

    [JsonPropertyName("merchant_customer_id")]
    public string? MerchantCustomerId { get; set; }
}

internal sealed class PayPalSetupTokenRequest
{
    public PayPalCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalVaultPaymentSource PaymentSource { get; set; } = new();
}

internal sealed class PayPalVaultPaymentSource
{
    public PayPalVaultCard? Card { get; set; }
    public PayPalVaultToken? Token { get; set; }
}

internal sealed class PayPalVaultCard
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("billing_address")]
    public PayPalAddress? BillingAddress { get; set; }
}

internal sealed class PayPalVaultToken
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "SETUP_TOKEN";
}

internal sealed class PayPalSetupTokenResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalCustomer? Customer { get; set; }
    public List<PayPalLink>? Links { get; set; }
}

internal sealed class PayPalPaymentTokenRequest
{
    public PayPalCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalVaultPaymentSource PaymentSource { get; set; } = new();
}

internal sealed class PayPalPaymentTokenResponse
{
    public string? Id { get; set; }
    public PayPalCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentTokenSource? PaymentSource { get; set; }
}

internal sealed class PayPalPaymentTokenSource
{
    public PayPalVaultedCard? Card { get; set; }
}

internal sealed class PayPalVaultedCard
{
    public string? Name { get; set; }

    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

internal sealed class PayPalSearchResponse
{
    [JsonPropertyName("transaction_details")]
    public List<PayPalTransactionDetail>? TransactionDetails { get; set; }

    public int? Page { get; set; }

    [JsonPropertyName("total_items")]
    public int? TotalItems { get; set; }

    [JsonPropertyName("total_pages")]
    public int? TotalPages { get; set; }
}

internal sealed class PayPalTransactionDetail
{
    [JsonPropertyName("transaction_info")]
    public PayPalTransactionInfo? TransactionInfo { get; set; }
}

internal sealed class PayPalTransactionInfo
{
    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("paypal_reference_id")]
    public string? PaypalReferenceId { get; set; }

    [JsonPropertyName("transaction_event_code")]
    public string? TransactionEventCode { get; set; }

    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }

    [JsonPropertyName("transaction_amount")]
    public PayPalMoney? TransactionAmount { get; set; }

    [JsonPropertyName("fee_amount")]
    public PayPalMoney? FeeAmount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_field")]
    public string? CustomField { get; set; }

    [JsonPropertyName("transaction_initiation_date")]
    public string? TransactionInitiationDate { get; set; }
}
