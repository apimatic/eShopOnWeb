using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;

internal sealed class OAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal sealed class PayPalErrorResponse
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

internal sealed class PayPalErrorDetail
{
    [JsonPropertyName("issue")]
    public string? Issue { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }
}

internal sealed class MoneyDto
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

internal sealed class LinkDto
{
    [JsonPropertyName("href")]
    public string? Href { get; set; }

    [JsonPropertyName("rel")]
    public string? Rel { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }
}

internal sealed class AddressDto
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

internal sealed class StoredCredentialDto
{
    [JsonPropertyName("payment_initiator")]
    public string? PaymentInitiator { get; set; }

    [JsonPropertyName("payment_type")]
    public string? PaymentType { get; set; }

    [JsonPropertyName("usage")]
    public string? Usage { get; set; }
}

internal sealed class CardRequestDto
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
    public AddressDto? BillingAddress { get; set; }

    [JsonPropertyName("vault_id")]
    public string? VaultId { get; set; }

    [JsonPropertyName("stored_credential")]
    public StoredCredentialDto? StoredCredential { get; set; }
}

internal sealed class PaymentSourceRequestDto
{
    [JsonPropertyName("card")]
    public CardRequestDto? Card { get; set; }
}

internal sealed class PurchaseUnitRequestDto
{
    [JsonPropertyName("reference_id")]
    public string? ReferenceId { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("amount")]
    public MoneyDto? Amount { get; set; }
}

internal sealed class CreateOrderRequestDto
{
    [JsonPropertyName("intent")]
    public string? Intent { get; set; }

    [JsonPropertyName("purchase_units")]
    public List<PurchaseUnitRequestDto>? PurchaseUnits { get; set; }

    [JsonPropertyName("payment_source")]
    public PaymentSourceRequestDto? PaymentSource { get; set; }
}

internal sealed class AuthorizationDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public MoneyDto? Amount { get; set; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }

    [JsonPropertyName("update_time")]
    public string? UpdateTime { get; set; }

    [JsonPropertyName("expiration_time")]
    public string? ExpirationTime { get; set; }

    [JsonPropertyName("status_details")]
    public StatusDetailsDto? StatusDetails { get; set; }
}

internal sealed class StatusDetailsDto
{
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

internal sealed class SellerReceivableBreakdownDto
{
    [JsonPropertyName("gross_amount")]
    public MoneyDto? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public MoneyDto? PaypalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public MoneyDto? NetAmount { get; set; }
}

internal sealed class CaptureDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public MoneyDto? Amount { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public SellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }

    [JsonPropertyName("update_time")]
    public string? UpdateTime { get; set; }
}

internal sealed class PaymentCollectionDto
{
    [JsonPropertyName("authorizations")]
    public List<AuthorizationDto>? Authorizations { get; set; }

    [JsonPropertyName("captures")]
    public List<CaptureDto>? Captures { get; set; }

    [JsonPropertyName("refunds")]
    public List<RefundDto>? Refunds { get; set; }
}

internal sealed class PurchaseUnitDto
{
    [JsonPropertyName("payments")]
    public PaymentCollectionDto? Payments { get; set; }
}

internal sealed class OrderDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("intent")]
    public string? Intent { get; set; }

    [JsonPropertyName("purchase_units")]
    public List<PurchaseUnitDto>? PurchaseUnits { get; set; }

    [JsonPropertyName("links")]
    public List<LinkDto>? Links { get; set; }
}

internal sealed class CaptureRequestDto
{
    [JsonPropertyName("amount")]
    public MoneyDto? Amount { get; set; }

    [JsonPropertyName("final_capture")]
    public bool FinalCapture { get; set; } = true;

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }
}

internal sealed class ReauthorizeRequestDto
{
    [JsonPropertyName("amount")]
    public MoneyDto? Amount { get; set; }
}

internal sealed class RefundRequestDto
{
    [JsonPropertyName("amount")]
    public MoneyDto? Amount { get; set; }
}

internal sealed class RefundDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public MoneyDto? Amount { get; set; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }
}

internal sealed class VaultCustomerDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("merchant_customer_id")]
    public string? MerchantCustomerId { get; set; }
}

internal sealed class VaultCardResponseDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }
}

internal sealed class VaultPaymentSourceResponseDto
{
    [JsonPropertyName("card")]
    public VaultCardResponseDto? Card { get; set; }
}

internal sealed class CreatePaymentTokenRequestDto
{
    [JsonPropertyName("customer")]
    public VaultCustomerDto? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PaymentSourceRequestDto? PaymentSource { get; set; }
}

internal sealed class PaymentTokenResponseDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("customer")]
    public VaultCustomerDto? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public VaultPaymentSourceResponseDto? PaymentSource { get; set; }
}

internal sealed class TransactionSearchResponseDto
{
    [JsonPropertyName("transaction_details")]
    public List<TransactionDetailDto>? TransactionDetails { get; set; }

    [JsonPropertyName("total_pages")]
    public int? TotalPages { get; set; }

    [JsonPropertyName("total_items")]
    public int? TotalItems { get; set; }

    [JsonPropertyName("page")]
    public int? Page { get; set; }
}

internal sealed class TransactionDetailDto
{
    [JsonPropertyName("transaction_info")]
    public TransactionInfoDto? TransactionInfo { get; set; }
}

internal sealed class TransactionInfoDto
{
    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("paypal_reference_id")]
    public string? PaypalReferenceId { get; set; }

    [JsonPropertyName("paypal_reference_id_type")]
    public string? PaypalReferenceIdType { get; set; }

    [JsonPropertyName("transaction_event_code")]
    public string? TransactionEventCode { get; set; }

    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_field")]
    public string? CustomField { get; set; }

    [JsonPropertyName("transaction_amount")]
    public MoneyDto? TransactionAmount { get; set; }

    [JsonPropertyName("fee_amount")]
    public MoneyDto? FeeAmount { get; set; }

    [JsonPropertyName("transaction_initiation_date")]
    public string? TransactionInitiationDate { get; set; }
}
