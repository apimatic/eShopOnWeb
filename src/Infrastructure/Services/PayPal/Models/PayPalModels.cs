using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal.Models;

// OAuth
public record OAuthTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("token_type")] string TokenType
);

// Shared
public record PayPalAmount(
    [property: JsonPropertyName("currency_code")] string CurrencyCode,
    [property: JsonPropertyName("value")] string Value
);

// Orders v2 - Create
public record CreateOrderRequest(
    [property: JsonPropertyName("intent")] string Intent,
    [property: JsonPropertyName("purchase_units")] List<PurchaseUnitRequest> PurchaseUnits
);

public record PurchaseUnitRequest(
    [property: JsonPropertyName("amount")] PayPalAmount Amount,
    [property: JsonPropertyName("custom_id")] string? CustomId = null
);

public record CreateOrderResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status
);

// Orders v2 - Authorize
public record AuthorizeOrderRequest(
    [property: JsonPropertyName("payment_source")] PaymentSourceRequest PaymentSource
);

public record PaymentSourceRequest
{
    [JsonPropertyName("card")]
    public CardPaymentSource? Card { get; init; }

    [JsonPropertyName("token")]
    public TokenPaymentSource? Token { get; init; }
}

public record CardPaymentSource(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("number")] string Number,
    [property: JsonPropertyName("expiry")] string Expiry,
    [property: JsonPropertyName("security_code")] string? SecurityCode,
    [property: JsonPropertyName("billing_address")] CardBillingAddress? BillingAddress = null
);

public record TokenPaymentSource(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type
);

public record CardBillingAddress(
    [property: JsonPropertyName("country_code")] string CountryCode,
    [property: JsonPropertyName("postal_code")] string? PostalCode = null,
    [property: JsonPropertyName("admin_area_1")] string? AdminArea1 = null,
    [property: JsonPropertyName("admin_area_2")] string? AdminArea2 = null,
    [property: JsonPropertyName("address_line_1")] string? AddressLine1 = null
);

public record AuthorizeOrderResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("purchase_units")] List<PurchaseUnitResponse>? PurchaseUnits
);

public record PurchaseUnitResponse(
    [property: JsonPropertyName("payments")] PaymentsResponse? Payments
);

public record PaymentsResponse(
    [property: JsonPropertyName("authorizations")] List<AuthorizationDetail>? Authorizations,
    [property: JsonPropertyName("captures")] List<CaptureDetail>? Captures
);

public record AuthorizationDetail(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("amount")] PayPalAmount? Amount,
    [property: JsonPropertyName("expiration_time")] string? ExpirationTime
);

// Payments v2 - Capture
public record CaptureAuthorizationRequest(
    [property: JsonPropertyName("final_capture")] bool FinalCapture = true
);

public record CaptureDetail(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("amount")] PayPalAmount? Amount,
    [property: JsonPropertyName("seller_receivable_breakdown")] SellerReceivableBreakdown? SellerReceivableBreakdown
);

public record SellerReceivableBreakdown(
    [property: JsonPropertyName("gross_amount")] PayPalAmount? GrossAmount,
    [property: JsonPropertyName("paypal_fee")] PayPalAmount? PayPalFee,
    [property: JsonPropertyName("net_amount")] PayPalAmount? NetAmount
);

// Payments v2 - Refund
public record RefundCaptureRequest
{
    [JsonPropertyName("amount")]
    public PayPalAmount? Amount { get; init; }

    [JsonPropertyName("note_to_payer")]
    public string? NoteToPayer { get; init; }
}

public record RefundResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("amount")] PayPalAmount? Amount,
    [property: JsonPropertyName("seller_payable_breakdown")] SellerPayableBreakdown? SellerPayableBreakdown
);

public record SellerPayableBreakdown(
    [property: JsonPropertyName("total_refunded_amount")] PayPalAmount? TotalRefundedAmount
);

// Payments v2 - Authorization (GET / void response)
public record AuthorizationResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("expiration_time")] string? ExpirationTime
);

// Payments v2 - Reauthorize
public record ReauthorizeRequest
{
    [JsonPropertyName("amount")]
    public PayPalAmount? Amount { get; init; }
}

// Vault v3 - Create payment token
public record CreateVaultTokenRequest(
    [property: JsonPropertyName("payment_source")] VaultPaymentSource PaymentSource,
    [property: JsonPropertyName("customer")] VaultCustomer? Customer = null
);

public record VaultPaymentSource(
    [property: JsonPropertyName("card")] VaultCardSource Card
);

public record VaultCardSource(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("number")] string Number,
    [property: JsonPropertyName("expiry")] string Expiry,
    [property: JsonPropertyName("security_code")] string? SecurityCode,
    [property: JsonPropertyName("billing_address")] VaultBillingAddress? BillingAddress = null
);

public record VaultBillingAddress(
    [property: JsonPropertyName("country_code")] string CountryCode,
    [property: JsonPropertyName("postal_code")] string? PostalCode = null
);

public record VaultCustomer(
    [property: JsonPropertyName("merchant_customer_id")] string MerchantCustomerId
);

public record VaultTokenResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("customer")] VaultCustomerResponse? Customer,
    [property: JsonPropertyName("payment_source")] VaultPaymentSourceResponse? PaymentSource
);

public record VaultCustomerResponse(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("merchant_customer_id")] string? MerchantCustomerId
);

public record VaultPaymentSourceResponse(
    [property: JsonPropertyName("card")] VaultCardResponse? Card
);

public record VaultCardResponse(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("last_digits")] string? LastDigits,
    [property: JsonPropertyName("brand")] string? Brand,
    [property: JsonPropertyName("expiry")] string? Expiry,
    [property: JsonPropertyName("type")] string? Type
);

// Transaction Search v1
public record TransactionSearchResponse(
    [property: JsonPropertyName("transaction_details")] List<TransactionDetail>? TransactionDetails,
    [property: JsonPropertyName("total_items")] int? TotalItems,
    [property: JsonPropertyName("total_pages")] int? TotalPages,
    [property: JsonPropertyName("page")] int? Page,
    [property: JsonPropertyName("account_number")] string? AccountNumber
);

public record TransactionDetail(
    [property: JsonPropertyName("transaction_info")] TransactionInfo? TransactionInfo
);

public record TransactionInfo(
    [property: JsonPropertyName("transaction_id")] string? TransactionId,
    [property: JsonPropertyName("paypal_reference_id")] string? PayPalReferenceId,
    [property: JsonPropertyName("paypal_reference_id_type")] string? PayPalReferenceIdType,
    [property: JsonPropertyName("transaction_event_code")] string? TransactionEventCode,
    [property: JsonPropertyName("transaction_initiation_date")] string? TransactionInitiationDate,
    [property: JsonPropertyName("transaction_updated_date")] string? TransactionUpdatedDate,
    [property: JsonPropertyName("transaction_amount")] PayPalAmount? TransactionAmount,
    [property: JsonPropertyName("fee_amount")] PayPalAmount? FeeAmount,
    [property: JsonPropertyName("transaction_status")] string? TransactionStatus,
    [property: JsonPropertyName("custom_field")] string? CustomField,
    [property: JsonPropertyName("invoice_id")] string? InvoiceId,
    [property: JsonPropertyName("ending_balance")] PayPalAmount? EndingBalance,
    [property: JsonPropertyName("available_balance")] PayPalAmount? AvailableBalance
);
