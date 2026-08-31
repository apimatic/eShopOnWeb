using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Wire DTOs for the PayPal REST APIs. Serialized with a snake_case naming policy,
// so PascalCase property names map to PayPal's snake_case JSON fields.

internal class OAuthTokenResponse
{
    public string? AccessToken { get; set; }
    public int ExpiresIn { get; set; }
}

internal class AmountDto
{
    public string? CurrencyCode { get; set; }
    public string? Value { get; set; }
}

internal class AddressDto
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

internal class CardDto
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? Name { get; set; }
    public string? SecurityCode { get; set; }
    public string? VaultId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public AddressDto? BillingAddress { get; set; }
}

internal class TokenDto
{
    public string? Id { get; set; }
    public string? Type { get; set; }
}

internal class PaymentSourceDto
{
    public CardDto? Card { get; set; }
    public TokenDto? Token { get; set; }
}

internal class PurchaseUnitDto
{
    public string? ReferenceId { get; set; }
    public string? CustomId { get; set; }
    public AmountDto? Amount { get; set; }
    public PaymentsDto? Payments { get; set; }
}

internal class PaymentsDto
{
    public List<AuthorizationDto>? Authorizations { get; set; }
    public List<CaptureDto>? Captures { get; set; }
}

internal class AuthorizationDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public AmountDto? Amount { get; set; }
    public DateTimeOffset? ExpirationTime { get; set; }
}

internal class SellerReceivableBreakdownDto
{
    public AmountDto? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public AmountDto? PayPalFee { get; set; }
    public AmountDto? NetAmount { get; set; }
}

internal class CaptureDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public AmountDto? Amount { get; set; }
    public SellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
}

internal class CreateOrderRequestDto
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PurchaseUnitDto> PurchaseUnits { get; set; } = new();
    public PaymentSourceDto? PaymentSource { get; set; }
}

internal class OrderResponseDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PaymentSourceDto? PaymentSource { get; set; }
    public List<PurchaseUnitDto>? PurchaseUnits { get; set; }
}

internal class CaptureRequestDto
{
    public AmountDto? Amount { get; set; }
    public bool FinalCapture { get; set; } = true;
}

internal class RefundRequestDto
{
    public AmountDto? Amount { get; set; }
}

internal class RefundResponseDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public AmountDto? Amount { get; set; }
}

internal class ReauthorizeRequestDto
{
    public AmountDto? Amount { get; set; }
}

internal class SetupTokenRequestDto
{
    public PaymentSourceDto? PaymentSource { get; set; }
}

internal class CustomerDto
{
    public string? Id { get; set; }
}

internal class SetupTokenResponseDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public CustomerDto? Customer { get; set; }
}

internal class PaymentTokenRequestDto
{
    public PaymentSourceDto? PaymentSource { get; set; }
}

internal class PaymentTokenResponseDto
{
    public string? Id { get; set; }
    public CustomerDto? Customer { get; set; }
    public PaymentSourceDto? PaymentSource { get; set; }
}

internal class TransactionInfoDto
{
    public string? TransactionId { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public DateTimeOffset? TransactionInitiationDate { get; set; }
    public AmountDto? TransactionAmount { get; set; }
    public AmountDto? FeeAmount { get; set; }
}

internal class TransactionDetailDto
{
    public TransactionInfoDto? TransactionInfo { get; set; }
}

internal class TransactionsResponseDto
{
    public List<TransactionDetailDto>? TransactionDetails { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
}

internal class ErrorDetailDto
{
    public string? Field { get; set; }
    public string? Issue { get; set; }
    public string? Description { get; set; }
}

internal class ErrorResponseDto
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public List<ErrorDetailDto>? Details { get; set; }
}
