using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

// OAuth token
public record PayPalTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("token_type")] string TokenType
);

// Shared money object
public record PayPalMoney(
    [property: JsonPropertyName("currency_code")] string CurrencyCode,
    [property: JsonPropertyName("value")] string Value
);

// ── Orders API ──────────────────────────────────────────────────────────────

public record PayPalCreateOrderRequest(
    [property: JsonPropertyName("intent")] string Intent,
    [property: JsonPropertyName("purchase_units")] List<PayPalPurchaseUnitRequest> PurchaseUnits
);

public record PayPalPurchaseUnitRequest(
    [property: JsonPropertyName("amount")] PayPalMoney Amount,
    [property: JsonPropertyName("custom_id")] string? CustomId = null,
    [property: JsonPropertyName("reference_id")] string? ReferenceId = null
);

public record PayPalOrderResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("purchase_units")] List<PayPalPurchaseUnitResponse>? PurchaseUnits = null
);

public record PayPalPurchaseUnitResponse(
    [property: JsonPropertyName("payments")] PayPalPaymentsResponse? Payments = null
);

public record PayPalPaymentsResponse(
    [property: JsonPropertyName("authorizations")] List<PayPalAuthorizationItem>? Authorizations = null,
    [property: JsonPropertyName("captures")] List<PayPalCaptureItem>? Captures = null
);

public record PayPalAuthorizationItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status
);

public record PayPalCaptureItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status
);

// Authorize order request
public record PayPalAuthorizeOrderRequest(
    [property: JsonPropertyName("payment_source")] PayPalOrderPaymentSource PaymentSource
);

public record PayPalOrderPaymentSource(
    [property: JsonPropertyName("card")] PayPalCardSource? Card = null
);

public record PayPalCardSource(
    [property: JsonPropertyName("number")] string? Number = null,
    [property: JsonPropertyName("expiry")] string? Expiry = null,
    [property: JsonPropertyName("security_code")] string? SecurityCode = null,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("billing_address")] PayPalAddress? BillingAddress = null,
    [property: JsonPropertyName("vault_id")] string? VaultId = null
);

public record PayPalAddress(
    [property: JsonPropertyName("country_code")] string CountryCode,
    [property: JsonPropertyName("address_line_1")] string? AddressLine1 = null,
    [property: JsonPropertyName("admin_area_2")] string? City = null,
    [property: JsonPropertyName("admin_area_1")] string? State = null,
    [property: JsonPropertyName("postal_code")] string? PostalCode = null
);

// ── Payments API ─────────────────────────────────────────────────────────────

public record PayPalCaptureRequest(
    [property: JsonPropertyName("final_capture")] bool FinalCapture = true,
    [property: JsonPropertyName("amount")] PayPalMoney? Amount = null
);

public record PayPalCaptureResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("seller_receivable_breakdown")] PayPalSellerReceivableBreakdown? SellerReceivableBreakdown = null
);

public record PayPalSellerReceivableBreakdown(
    [property: JsonPropertyName("gross_amount")] PayPalMoney? GrossAmount = null,
    [property: JsonPropertyName("paypal_fee")] PayPalMoney? PaypalFee = null,
    [property: JsonPropertyName("net_amount")] PayPalMoney? NetAmount = null
);

public record PayPalGetAuthorizationResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status
);

public record PayPalReauthorizeRequest(
    [property: JsonPropertyName("amount")] PayPalMoney Amount
);

public record PayPalReauthorizeResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status
);

public record PayPalRefundRequest(
    [property: JsonPropertyName("amount")] PayPalMoney? Amount = null,
    [property: JsonPropertyName("note_to_payer")] string? NoteToPayer = null
);

public record PayPalRefundResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status
);

// ── Vault API ─────────────────────────────────────────────────────────────────

public record PayPalCreateVaultTokenRequest(
    [property: JsonPropertyName("payment_source")] PayPalVaultPaymentSource PaymentSource,
    [property: JsonPropertyName("customer")] PayPalVaultCustomer? Customer = null
);

public record PayPalVaultPaymentSource(
    [property: JsonPropertyName("card")] PayPalVaultCardRequest? Card = null
);

public record PayPalVaultCardRequest(
    [property: JsonPropertyName("number")] string? Number = null,
    [property: JsonPropertyName("expiry")] string? Expiry = null,
    [property: JsonPropertyName("security_code")] string? SecurityCode = null,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("billing_address")] PayPalAddress? BillingAddress = null
);

public record PayPalVaultCustomer(
    [property: JsonPropertyName("id")] string Id
);

public record PayPalVaultTokenResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("payment_source")] PayPalVaultTokenPaymentSource? PaymentSource = null,
    [property: JsonPropertyName("customer")] PayPalVaultCustomerResponse? Customer = null
);

public record PayPalVaultTokenPaymentSource(
    [property: JsonPropertyName("card")] PayPalVaultCardDetails? Card = null
);

public record PayPalVaultCardDetails(
    [property: JsonPropertyName("last_digits")] string? LastDigits = null,
    [property: JsonPropertyName("brand")] string? Brand = null,
    [property: JsonPropertyName("expiry")] string? Expiry = null
);

public record PayPalVaultCustomerResponse(
    [property: JsonPropertyName("id")] string Id
);

public record PayPalListVaultTokensResponse(
    [property: JsonPropertyName("payment_tokens")] List<PayPalVaultTokenResponse>? PaymentTokens = null,
    [property: JsonPropertyName("total_items")] int TotalItems = 0,
    [property: JsonPropertyName("total_pages")] int TotalPages = 0
);

// ── Transaction Search API ────────────────────────────────────────────────────

public record PayPalTransactionSearchResponse(
    [property: JsonPropertyName("transaction_details")] List<PayPalTransactionDetail>? TransactionDetails = null,
    [property: JsonPropertyName("total_items")] int TotalItems = 0,
    [property: JsonPropertyName("total_pages")] int TotalPages = 0,
    [property: JsonPropertyName("last_refreshed_datetime")] string? LastRefreshedDatetime = null
);

public record PayPalTransactionDetail(
    [property: JsonPropertyName("transaction_info")] PayPalTransactionInfo? TransactionInfo = null,
    [property: JsonPropertyName("payer_info")] PayPalPayerInfo? PayerInfo = null
);

public record PayPalTransactionInfo(
    [property: JsonPropertyName("paypal_account_id")] string? PaypalAccountId = null,
    [property: JsonPropertyName("transaction_id")] string? TransactionId = null,
    [property: JsonPropertyName("paypal_reference_id")] string? PaypalReferenceId = null,
    [property: JsonPropertyName("paypal_reference_id_type")] string? PaypalReferenceIdType = null,
    [property: JsonPropertyName("transaction_event_code")] string? TransactionEventCode = null,
    [property: JsonPropertyName("transaction_initiation_date")] string? TransactionInitiationDate = null,
    [property: JsonPropertyName("transaction_updated_date")] string? TransactionUpdatedDate = null,
    [property: JsonPropertyName("transaction_amount")] PayPalMoney? TransactionAmount = null,
    [property: JsonPropertyName("fee_amount")] PayPalMoney? FeeAmount = null,
    [property: JsonPropertyName("transaction_status")] string? TransactionStatus = null,
    [property: JsonPropertyName("custom_field")] string? CustomField = null,
    [property: JsonPropertyName("invoice_id")] string? InvoiceId = null,
    [property: JsonPropertyName("transaction_note")] string? TransactionNote = null
);

public record PayPalPayerInfo(
    [property: JsonPropertyName("account_id")] string? AccountId = null,
    [property: JsonPropertyName("email_address")] string? EmailAddress = null,
    [property: JsonPropertyName("payer_name")] PayPalPayerName? PayerName = null
);

public record PayPalPayerName(
    [property: JsonPropertyName("given_name")] string? GivenName = null,
    [property: JsonPropertyName("surname")] string? Surname = null
);

// PayPal error response
public record PayPalErrorResponse(
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("debug_id")] string? DebugId = null,
    [property: JsonPropertyName("details")] List<PayPalErrorDetail>? Details = null
);

public record PayPalErrorDetail(
    [property: JsonPropertyName("issue")] string? Issue = null,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("field")] string? Field = null
);
