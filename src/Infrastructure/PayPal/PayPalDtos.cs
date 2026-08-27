using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Hand-written DTOs matching the PayPal OpenAPI specifications in api-specs/paypal.
// Serialized with the System.Text.Json snake_case naming policy, so C# PascalCase property
// names map onto the spec's snake_case field names (e.g. SecurityCode -> security_code).

internal class PayPalTokenResponse
{
    public string? AccessToken { get; set; }
    public string? TokenType { get; set; }
    public int ExpiresIn { get; set; }
}

internal class MoneyDto
{
    public string? CurrencyCode { get; set; }
    public string? Value { get; set; }
}

internal class AddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

internal class CardRequestDto
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public AddressDto? BillingAddress { get; set; }
    public string? VaultId { get; set; }
    public StoredCredentialDto? StoredCredential { get; set; }
}

internal class StoredCredentialDto
{
    public string? PaymentInitiator { get; set; }
    public string? PaymentType { get; set; }
    public string? Usage { get; set; }
}

internal class PaymentSourceRequestDto
{
    public CardRequestDto? Card { get; set; }
}

internal class PurchaseUnitRequestDto
{
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public MoneyDto? Amount { get; set; }
}

internal class CreateOrderRequestDto
{
    public string? Intent { get; set; }
    public List<PurchaseUnitRequestDto>? PurchaseUnits { get; set; }
    public PaymentSourceRequestDto? PaymentSource { get; set; }
}

internal class AuthorizationDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public MoneyDto? Amount { get; set; }
    public string? ExpirationTime { get; set; }
}

internal class SellerReceivableBreakdownDto
{
    public MoneyDto? GrossAmount { get; set; }
    // Named "PaypalFee" so the snake_case naming policy produces the spec's "paypal_fee".
    public MoneyDto? PaypalFee { get; set; }
    public MoneyDto? NetAmount { get; set; }
}

internal class CaptureDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public MoneyDto? Amount { get; set; }
    public bool? FinalCapture { get; set; }
    public SellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
}

internal class RefundDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public MoneyDto? Amount { get; set; }
}

internal class PaymentsDto
{
    public List<AuthorizationDto>? Authorizations { get; set; }
}

internal class PurchaseUnitDto
{
    public PaymentsDto? Payments { get; set; }
}

internal class OrderDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public List<PurchaseUnitDto>? PurchaseUnits { get; set; }
}

internal class CaptureRequestDto
{
    public MoneyDto? Amount { get; set; }
    public string? InvoiceId { get; set; }
    public bool? FinalCapture { get; set; }
}

internal class ReauthorizeRequestDto
{
    public MoneyDto? Amount { get; set; }
}

internal class RefundRequestDto
{
    public MoneyDto? Amount { get; set; }
    public string? CustomId { get; set; }
    public string? NoteToPayer { get; set; }
}

internal class VaultCardDto
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public AddressDto? BillingAddress { get; set; }
}

internal class VaultPaymentSourceDto
{
    public VaultCardDto? Card { get; set; }
}

internal class VaultCustomerDto
{
    public string? MerchantCustomerId { get; set; }
}

internal class VaultTokenRequestDto
{
    public VaultPaymentSourceDto? PaymentSource { get; set; }
    public VaultCustomerDto? Customer { get; set; }
}

internal class VaultCardResponseDto
{
    public string? Name { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
}

internal class VaultPaymentSourceResponseDto
{
    public VaultCardResponseDto? Card { get; set; }
}

internal class VaultTokenResponseDto
{
    public string? Id { get; set; }
    public VaultPaymentSourceResponseDto? PaymentSource { get; set; }
}

internal class TransactionInfoDto
{
    public string? TransactionId { get; set; }
    public string? PaypalReferenceId { get; set; }
    public string? PaypalReferenceIdType { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionInitiationDate { get; set; }
    public string? TransactionUpdatedDate { get; set; }
    public MoneyDto? TransactionAmount { get; set; }
    public MoneyDto? FeeAmount { get; set; }
    public string? TransactionStatus { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
}

internal class TransactionDetailDto
{
    public TransactionInfoDto? TransactionInfo { get; set; }
}

internal class TransactionSearchResponseDto
{
    public List<TransactionDetailDto>? TransactionDetails { get; set; }
    public int? Page { get; set; }
    public int? TotalPages { get; set; }
    public int? TotalItems { get; set; }
}

internal class PayPalErrorDetailDto
{
    public string? Issue { get; set; }
    public string? Description { get; set; }
    public string? Field { get; set; }
}

internal class PayPalErrorDto
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public List<PayPalErrorDetailDto>? Details { get; set; }
}
