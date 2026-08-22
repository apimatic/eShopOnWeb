using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Contracts;

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
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public List<PayPalErrorDetail>? Details { get; set; }
}

internal sealed class PayPalErrorDetail
{
    public string? Field { get; set; }
    public string? Value { get; set; }
    public string? Issue { get; set; }
    public string? Description { get; set; }
}

internal sealed class MoneyDto
{
    public string? CurrencyCode { get; set; }
    public string? Value { get; set; }
}

internal sealed class OrderRequestDto
{
    public required string Intent { get; set; }
    public required List<PurchaseUnitRequestDto> PurchaseUnits { get; set; }
    public PaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PurchaseUnitRequestDto
{
    public required MoneyDto Amount { get; set; }
    public string? CustomId { get; set; }
    public string? InvoiceId { get; set; }
    public string? Description { get; set; }
}

internal sealed class PaymentSourceDto
{
    public CardRequestDto? Card { get; set; }
}

internal sealed class CardRequestDto
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
    public string? VaultId { get; set; }
    public StoredCredentialDto? StoredCredential { get; set; }
}

internal sealed class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

internal sealed class StoredCredentialDto
{
    public string? PaymentInitiator { get; set; }
    public string? PaymentType { get; set; }
    public string? Usage { get; set; }
}

internal sealed class OrderResponseDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public List<PurchaseUnitDto>? PurchaseUnits { get; set; }
    public List<LinkDto>? Links { get; set; }
}

internal sealed class PurchaseUnitDto
{
    public string? CustomId { get; set; }
    public string? InvoiceId { get; set; }
    public PaymentCollectionDto? Payments { get; set; }
}

internal sealed class PaymentCollectionDto
{
    public List<AuthorizationDto>? Authorizations { get; set; }
    public List<CaptureDto>? Captures { get; set; }
    public List<RefundDto>? Refunds { get; set; }
}

internal sealed class AuthorizationDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public MoneyDto? Amount { get; set; }
    public string? ExpirationTime { get; set; }
    public string? CreateTime { get; set; }
}

internal sealed class CaptureDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public MoneyDto? Amount { get; set; }
    public SellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
    public SupplementaryDataDto? SupplementaryData { get; set; }
}

internal sealed class SupplementaryDataDto
{
    public RelatedIdsDto? RelatedIds { get; set; }
}

internal sealed class RelatedIdsDto
{
    public string? OrderId { get; set; }
    public string? AuthorizationId { get; set; }
}

internal sealed class SellerReceivableBreakdownDto
{
    public MoneyDto? GrossAmount { get; set; }
    public MoneyDto? PaypalFee { get; set; }
    public MoneyDto? NetAmount { get; set; }
}

internal sealed class CaptureRequestDto
{
    public MoneyDto? Amount { get; set; }
    public bool FinalCapture { get; set; } = true;
}

internal sealed class ReauthorizeRequestDto
{
    public MoneyDto? Amount { get; set; }
}

internal sealed class RefundRequestDto
{
    public MoneyDto? Amount { get; set; }
}

internal sealed class RefundDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public MoneyDto? Amount { get; set; }
}

internal sealed class PaymentTokenRequestDto
{
    public VaultCustomerDto? Customer { get; set; }
    public required VaultPaymentSourceDto PaymentSource { get; set; }
}

internal sealed class VaultCustomerDto
{
    public string? Id { get; set; }
    public string? MerchantCustomerId { get; set; }
}

internal sealed class VaultPaymentSourceDto
{
    public required VaultCardRequestDto Card { get; set; }
}

internal sealed class VaultCardRequestDto
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

internal sealed class PaymentTokenResponseDto
{
    public string? Id { get; set; }
    public VaultCustomerDto? Customer { get; set; }
    public PaymentTokenSourceResponseDto? PaymentSource { get; set; }
}

internal sealed class PaymentTokenSourceResponseDto
{
    public CardResponseDto? Card { get; set; }
}

internal sealed class CardResponseDto
{
    public string? Name { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

internal sealed class SearchResponseDto
{
    public List<TransactionDetailDto>? TransactionDetails { get; set; }
    public int? Page { get; set; }
    public int? TotalItems { get; set; }
    public int? TotalPages { get; set; }
}

internal sealed class TransactionDetailDto
{
    public TransactionInfoDto? TransactionInfo { get; set; }
}

internal sealed class TransactionInfoDto
{
    public string? TransactionId { get; set; }
    public string? PaypalReferenceId { get; set; }
    public string? CustomField { get; set; }
    public string? InvoiceId { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public MoneyDto? TransactionAmount { get; set; }
    public string? TransactionInitiationDate { get; set; }
}

internal sealed class LinkDto
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}
