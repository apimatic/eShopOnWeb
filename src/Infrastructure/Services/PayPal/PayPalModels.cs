using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

// ── OAuth ──────────────────────────────────────────────────────────────────

internal class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

// ── Common ─────────────────────────────────────────────────────────────────

internal class Money
{
    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}

// ── Checkout Orders v2 ─────────────────────────────────────────────────────

internal class CreateOrderRequest
{
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "AUTHORIZE";

    [JsonPropertyName("purchase_units")]
    public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();

    [JsonPropertyName("payment_source")]
    public PaymentSource? PaymentSource { get; set; }
}

internal class PurchaseUnitRequest
{
    [JsonPropertyName("amount")]
    public Money Amount { get; set; } = new();

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }
}

internal class PaymentSource
{
    [JsonPropertyName("card")]
    public CardRequest? Card { get; set; }
}

internal class CardRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("billing_address")]
    public CardAddress? BillingAddress { get; set; }

    [JsonPropertyName("vault_id")]
    public string? VaultId { get; set; }

    [JsonPropertyName("stored_credential")]
    public StoredCredential? StoredCredential { get; set; }

    [JsonPropertyName("attributes")]
    public CardAttributes? Attributes { get; set; }
}

internal class CardAddress
{
    [JsonPropertyName("address_line_1")]
    public string? AddressLine1 { get; set; }

    [JsonPropertyName("admin_area_2")]
    public string? City { get; set; }

    [JsonPropertyName("admin_area_1")]
    public string? State { get; set; }

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }
}

internal class StoredCredential
{
    [JsonPropertyName("payment_initiator")]
    public string PaymentInitiator { get; set; } = "CUSTOMER";

    [JsonPropertyName("payment_type")]
    public string PaymentType { get; set; } = "UNSCHEDULED";

    [JsonPropertyName("usage")]
    public string Usage { get; set; } = "SUBSEQUENT";
}

internal class CardAttributes
{
    [JsonPropertyName("customer")]
    public CardCustomer? Customer { get; set; }
}

internal class CardCustomer
{
    [JsonPropertyName("merchant_customer_id")]
    public string? MerchantCustomerId { get; set; }
}

internal class OrderResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("purchase_units")]
    public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }
}

internal class PurchaseUnitResponse
{
    [JsonPropertyName("payments")]
    public PaymentSummary? Payments { get; set; }
}

internal class PaymentSummary
{
    [JsonPropertyName("authorizations")]
    public List<AuthorizationSummary>? Authorizations { get; set; }
}

internal class AuthorizationSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("expiration_time")]
    public string? ExpirationTime { get; set; }

    [JsonPropertyName("amount")]
    public Money? Amount { get; set; }
}

// ── Payments v2 ────────────────────────────────────────────────────────────

internal class CaptureAuthorizationRequest
{
    [JsonPropertyName("final_capture")]
    public bool FinalCapture { get; set; } = true;
}

internal class CaptureResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("amount")]
    public Money? Amount { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public SellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal class SellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")]
    public Money? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public Money? PayPalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public Money? NetAmount { get; set; }
}

internal class AuthorizationDetailsResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("amount")]
    public Money? Amount { get; set; }

    [JsonPropertyName("expiration_time")]
    public string? ExpirationTime { get; set; }
}

internal class ReauthorizeResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}

internal class RefundRequest
{
    [JsonPropertyName("amount")]
    public Money? Amount { get; set; }

    [JsonPropertyName("note_to_payer")]
    public string? NoteToPayer { get; set; }
}

internal class RefundResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("amount")]
    public Money? Amount { get; set; }
}

// ── Vault v3 ───────────────────────────────────────────────────────────────

internal class SetupTokenRequest
{
    [JsonPropertyName("customer")]
    public VaultCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public VaultPaymentSource PaymentSource { get; set; } = new();
}

internal class VaultCustomer
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("merchant_customer_id")]
    public string? MerchantCustomerId { get; set; }
}

internal class VaultPaymentSource
{
    [JsonPropertyName("card")]
    public VaultCard? Card { get; set; }

    [JsonPropertyName("token")]
    public VaultToken? Token { get; set; }
}

internal class VaultCard
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("billing_address")]
    public VaultAddress? BillingAddress { get; set; }

    [JsonPropertyName("verification_method")]
    public string? VerificationMethod { get; set; }
}

internal class VaultAddress
{
    [JsonPropertyName("address_line_1")]
    public string? AddressLine1 { get; set; }

    [JsonPropertyName("admin_area_2")]
    public string? City { get; set; }

    [JsonPropertyName("admin_area_1")]
    public string? State { get; set; }

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }
}

internal class VaultToken
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "SETUP_TOKEN";
}

internal class SetupTokenResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("customer")]
    public VaultCustomer? Customer { get; set; }
}

internal class PaymentTokenRequest
{
    [JsonPropertyName("customer")]
    public VaultCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public VaultPaymentSource PaymentSource { get; set; } = new();
}

internal class PaymentTokenResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("customer")]
    public VaultCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public VaultPaymentSourceResponse? PaymentSource { get; set; }
}

internal class VaultPaymentSourceResponse
{
    [JsonPropertyName("card")]
    public VaultCardResponse? Card { get; set; }
}

internal class VaultCardResponse
{
    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal class CustomerPaymentTokensResponse
{
    [JsonPropertyName("payment_tokens")]
    public List<PaymentTokenResponse>? PaymentTokens { get; set; }

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }
}

// ── Transaction Search v1 ──────────────────────────────────────────────────

internal class TransactionSearchResponse
{
    [JsonPropertyName("transaction_details")]
    public List<TransactionDetailItem>? TransactionDetails { get; set; }

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }
}

internal class TransactionDetailItem
{
    [JsonPropertyName("transaction_info")]
    public TransactionInfoItem? TransactionInfo { get; set; }

    [JsonPropertyName("payer_info")]
    public PayerInfoItem? PayerInfo { get; set; }
}

internal class TransactionInfoItem
{
    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("paypal_reference_id")]
    public string? PayPalReferenceId { get; set; }

    [JsonPropertyName("paypal_reference_id_type")]
    public string? PayPalReferenceIdType { get; set; }

    [JsonPropertyName("transaction_event_code")]
    public string? TransactionEventCode { get; set; }

    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }

    [JsonPropertyName("transaction_amount")]
    public Money? TransactionAmount { get; set; }

    [JsonPropertyName("fee_amount")]
    public Money? FeeAmount { get; set; }

    [JsonPropertyName("transaction_initiation_date")]
    public string? TransactionInitiationDate { get; set; }

    [JsonPropertyName("custom_field")]
    public string? CustomField { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }
}

internal class PayerInfoItem
{
    [JsonPropertyName("email_address")]
    public string? EmailAddress { get; set; }
}

// ── PayPal Error ───────────────────────────────────────────────────────────

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

internal class PayPalErrorDetail
{
    [JsonPropertyName("issue")]
    public string? Issue { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
