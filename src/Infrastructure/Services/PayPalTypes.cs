using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services;

// ── Input types ─────────────────────────────────────────────────────────────

public class CardDetails
{
    public string Number { get; set; } = "";
    public string Expiry { get; set; } = "";       // YYYY-MM
    public string SecurityCode { get; set; } = "";
    public string Name { get; set; } = "";
    public CardBillingAddress? BillingAddress { get; set; }
}

public class CardBillingAddress
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string CountryCode { get; set; } = "US";
}

// ── Result types (returned to callers) ──────────────────────────────────────

public class PayPalOrderResult
{
    public string PayPalOrderId { get; set; } = "";
    public string AuthorizationId { get; set; } = "";
    public string AuthorizationStatus { get; set; } = "";
    public DateTimeOffset AuthorizationExpiry { get; set; }
    public DateTimeOffset AuthorizationCreatedAt { get; set; }
}

public class PayPalCaptureResult
{
    public string CaptureId { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal CapturedAmount { get; set; }
    public decimal PayPalFee { get; set; }
    public decimal NetAmount { get; set; }
}

public class PayPalReauthorizeResult
{
    public string NewAuthorizationId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset ExpirationTime { get; set; }
}

public class PayPalRefundResult
{
    public string RefundId { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
}

public class PayPalSetupTokenResult
{
    public string SetupTokenId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string LastFour { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Expiry { get; set; } = "";
}

public class PayPalVaultTokenResult
{
    public string VaultId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string LastFour { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Expiry { get; set; } = "";
}

public class PayPalTransactionRecord
{
    public string TransactionId { get; set; } = "";
    public string EventCode { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal FeeAmount { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public string PayerEmail { get; set; } = "";
}

// ── PayPal API response DTOs (internal) ─────────────────────────────────────

public class PayPalTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

public class PayPalErrorResponse
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

public class PayPalErrorDetail
{
    [JsonPropertyName("issue")]
    public string? Issue { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class PayPalOrderResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }
}

public class PayPalPurchaseUnit
{
    [JsonPropertyName("payments")]
    public PayPalPayments? Payments { get; set; }
}

public class PayPalPayments
{
    [JsonPropertyName("authorizations")]
    public List<PayPalAuthorizationInfo>? Authorizations { get; set; }
}

public class PayPalAuthorizationInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("expiration_time")]
    public DateTimeOffset? ExpirationTime { get; set; }

    [JsonPropertyName("create_time")]
    public DateTimeOffset? CreateTime { get; set; }
}

public class PayPalCaptureResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

public class PayPalSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")]
    public PayPalAmount? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public PayPalAmount? PaypalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public PayPalAmount? NetAmount { get; set; }
}

public class PayPalAmount
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

public class PayPalReauthorizeResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("expiration_time")]
    public DateTimeOffset? ExpirationTime { get; set; }
}

public class PayPalRefundResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalAmount? Amount { get; set; }
}

public class PayPalSetupTokenResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("customer")]
    public PayPalCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalVaultPaymentSource? PaymentSource { get; set; }
}

public class PayPalCustomer
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

public class PayPalVaultPaymentSource
{
    [JsonPropertyName("card")]
    public PayPalVaultCardInfo? Card { get; set; }
}

public class PayPalVaultCardInfo
{
    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class PayPalVaultTokenResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("customer")]
    public PayPalCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalVaultPaymentSource? PaymentSource { get; set; }
}

public class PayPalListVaultTokensResponse
{
    [JsonPropertyName("payment_tokens")]
    public List<PayPalVaultTokenResponse>? PaymentTokens { get; set; }
}

public class PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")]
    public List<PayPalTransactionDetail>? TransactionDetails { get; set; }

    [JsonPropertyName("total_pages")]
    public int? TotalPages { get; set; }

    [JsonPropertyName("total_items")]
    public int? TotalItems { get; set; }
}

public class PayPalTransactionDetail
{
    [JsonPropertyName("transaction_info")]
    public PayPalTransactionInfo? TransactionInfo { get; set; }

    [JsonPropertyName("payer_info")]
    public PayPalPayerInfo? PayerInfo { get; set; }
}

public class PayPalTransactionInfo
{
    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("transaction_event_code")]
    public string? TransactionEventCode { get; set; }

    [JsonPropertyName("transaction_amount")]
    public PayPalAmount? TransactionAmount { get; set; }

    [JsonPropertyName("fee_amount")]
    public PayPalAmount? FeeAmount { get; set; }

    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }

    [JsonPropertyName("transaction_initiation_date")]
    public DateTimeOffset? TransactionInitiationDate { get; set; }
}

public class PayPalPayerInfo
{
    [JsonPropertyName("email_address")]
    public string? EmailAddress { get; set; }
}
