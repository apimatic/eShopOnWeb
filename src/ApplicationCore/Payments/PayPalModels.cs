using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class CardPaymentSource
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public string? Name { get; init; }
    public CardBillingAddress? BillingAddress { get; init; }
}

public sealed class CardBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
    public required string CountryCode { get; init; }
}

public sealed class PayPalMoney
{
    public required string CurrencyCode { get; init; }
    public required string Value { get; init; }

    public decimal ToDecimal() =>
        decimal.Parse(Value, System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class PayPalAuthorizationSnapshot
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required PayPalMoney Amount { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
}

public sealed class PayPalCaptureSnapshot
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required PayPalMoney Amount { get; init; }
    public PayPalMoney? PayPalFee { get; init; }
    public PayPalMoney? NetAmount { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
}

public sealed class PayPalRefundSnapshot
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required PayPalMoney Amount { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
}

public sealed class PayPalOrderSnapshot
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public PayPalAuthorizationSnapshot? Authorization { get; init; }
    public IReadOnlyList<string> PayerActionLinks { get; init; } = Array.Empty<string>();
}

public sealed class PayPalVaultedCard
{
    public required string PaymentTokenId { get; init; }
    public required string LastDigits { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
    public string? CustomerId { get; init; }
}

public sealed class PayPalReportedTransaction
{
    public string? TransactionId { get; init; }
    public string? ReferenceId { get; init; }
    public string? ReferenceIdType { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public DateTimeOffset? UpdatedDate { get; init; }
    public PayPalMoney? Amount { get; init; }
    public PayPalMoney? FeeAmount { get; init; }
}

public sealed class CreatePayPalAuthorizeRequest
{
    public required string InvoiceId { get; init; }
    public required string CustomId { get; init; }
    public required string CurrencyCode { get; init; }
    public required string AmountValue { get; init; }
    public CardPaymentSource? Card { get; init; }
    public string? VaultId { get; init; }
}
