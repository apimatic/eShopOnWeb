using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record CardInput(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    BillingAddressInput BillingAddress);

public sealed record BillingAddressInput(
    string AddressLine1,
    string? AddressLine2,
    string AdminArea2,
    string AdminArea1,
    string PostalCode,
    string CountryCode);

public sealed record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpirationTime,
    string? Brand,
    string? LastDigits);

public sealed record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? Fee,
    decimal? NetAmount,
    DateTimeOffset CreatedAt);

public sealed record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record PayPalSavedCardResult(
    string PaymentTokenId,
    string? CustomerId,
    string Brand,
    string LastDigits,
    string Expiry);

public sealed record PayPalTransactionPage(
    IReadOnlyList<PayPalTransaction> Transactions,
    int Page,
    int TotalPages);

public sealed record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string EventCode,
    string Status,
    DateTimeOffset InitiatedAt,
    DateTimeOffset UpdatedAt,
    decimal Amount,
    string Currency,
    decimal? Fee,
    string? InvoiceId);

internal sealed class MoneyDto
{
    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

internal sealed class PayPalLinkDto
{
    [JsonPropertyName("rel")]
    public string Rel { get; set; } = string.Empty;

    [JsonPropertyName("href")]
    public string Href { get; set; } = string.Empty;
}

internal sealed class PayPalOrderDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("payment_source")]
    public PaymentSourceResponseDto? PaymentSource { get; set; }

    [JsonPropertyName("purchase_units")]
    public List<PurchaseUnitResponseDto> PurchaseUnits { get; set; } = new();

    [JsonPropertyName("links")]
    public List<PayPalLinkDto> Links { get; set; } = new();
}

internal sealed class PaymentSourceResponseDto
{
    [JsonPropertyName("card")]
    public CardResponseDto? Card { get; set; }
}

internal sealed class CardResponseDto
{
    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }
}

internal sealed class PurchaseUnitResponseDto
{
    [JsonPropertyName("payments")]
    public PaymentsResponseDto? Payments { get; set; }
}

internal sealed class PaymentsResponseDto
{
    [JsonPropertyName("authorizations")]
    public List<AuthorizationResponseDto> Authorizations { get; set; } = new();
}

internal sealed class AuthorizationResponseDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public MoneyDto? Amount { get; set; }

    [JsonPropertyName("create_time")]
    public DateTimeOffset? CreateTime { get; set; }

    [JsonPropertyName("expiration_time")]
    public DateTimeOffset? ExpirationTime { get; set; }
}

internal sealed class CaptureResponseDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public MoneyDto? Amount { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public SellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }

    [JsonPropertyName("create_time")]
    public DateTimeOffset? CreateTime { get; set; }
}

internal sealed class SellerReceivableBreakdownDto
{
    [JsonPropertyName("gross_amount")]
    public MoneyDto? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public MoneyDto? PayPalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public MoneyDto? NetAmount { get; set; }
}

internal sealed class RefundResponseDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public MoneyDto? Amount { get; set; }

    [JsonPropertyName("create_time")]
    public DateTimeOffset? CreateTime { get; set; }
}

internal sealed class VaultTokenResponseDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("customer")]
    public VaultCustomerDto? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PaymentSourceResponseDto? PaymentSource { get; set; }

    [JsonPropertyName("links")]
    public List<PayPalLinkDto> Links { get; set; } = new();
}

internal sealed class VaultCustomerDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

internal sealed class TransactionSearchResponseDto
{
    [JsonPropertyName("transaction_details")]
    public List<TransactionDetailDto> TransactionDetails { get; set; } = new();

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }
}

internal sealed class TransactionDetailDto
{
    [JsonPropertyName("transaction_info")]
    public TransactionInfoDto TransactionInfo { get; set; } = new();
}

internal sealed class TransactionInfoDto
{
    [JsonPropertyName("transaction_id")]
    public string TransactionId { get; set; } = string.Empty;

    [JsonPropertyName("paypal_reference_id")]
    public string? PayPalReferenceId { get; set; }

    [JsonPropertyName("paypal_reference_id_type")]
    public string? PayPalReferenceIdType { get; set; }

    [JsonPropertyName("transaction_event_code")]
    public string TransactionEventCode { get; set; } = string.Empty;

    [JsonPropertyName("transaction_status")]
    public string TransactionStatus { get; set; } = string.Empty;

    [JsonPropertyName("transaction_initiation_date")]
    public DateTimeOffset TransactionInitiationDate { get; set; }

    [JsonPropertyName("transaction_updated_date")]
    public DateTimeOffset TransactionUpdatedDate { get; set; }

    [JsonPropertyName("transaction_amount")]
    public MoneyDto? TransactionAmount { get; set; }

    [JsonPropertyName("fee_amount")]
    public MoneyDto? FeeAmount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }
}
