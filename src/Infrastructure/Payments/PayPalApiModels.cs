using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class PayPalAccessTokenResponse
{
    public string? AccessToken { get; set; }
    public string? TokenType { get; set; }
    public int ExpiresIn { get; set; }
}

internal sealed class PayPalErrorBody
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public List<PayPalErrorDetail>? Details { get; set; }
}

internal sealed class PayPalErrorDetail
{
    public string? Field { get; set; }
    public string? Issue { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
}

internal sealed class PayPalMoneyAmount
{
    public string? CurrencyCode { get; set; }
    public string? Value { get; set; }
}

internal sealed class PayPalOrderRequest
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PayPalPurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    public PayPalPaymentSource? PaymentSource { get; set; }
}

internal sealed class PayPalPurchaseUnitRequest
{
    public string? ReferenceId { get; set; }
    public string? CustomId { get; set; }
    public string? InvoiceId { get; set; }
    public string? Description { get; set; }
    public PayPalMoneyAmount Amount { get; set; } = new();
}

internal sealed class PayPalPaymentSource
{
    public PayPalCardRequest? Card { get; set; }
}

internal sealed class PayPalCardRequest
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public PayPalCardAddress? BillingAddress { get; set; }
    public string? VaultId { get; set; }
    public PayPalStoredCredential? StoredCredential { get; set; }
}

internal sealed class PayPalCardAddress
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

internal sealed class PayPalStoredCredential
{
    public string PaymentInitiator { get; set; } = "CUSTOMER";
    public string PaymentType { get; set; } = "ONE_TIME";
    public string? Usage { get; set; }
}

internal sealed class PayPalOrderResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public string? Intent { get; set; }
    public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }
}

internal sealed class PayPalPurchaseUnit
{
    public PayPalPaymentCollection? Payments { get; set; }
}

internal sealed class PayPalPaymentCollection
{
    public List<PayPalAuthorizationResource>? Authorizations { get; set; }
    public List<PayPalCaptureResource>? Captures { get; set; }
}

internal sealed class PayPalAuthorizationResource
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyAmount? Amount { get; set; }
    public DateTimeOffset? CreateTime { get; set; }
    public DateTimeOffset? ExpirationTime { get; set; }
}

internal sealed class PayPalCaptureResource
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyAmount? Amount { get; set; }
    public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal sealed class PayPalSellerReceivableBreakdown
{
    public PayPalMoneyAmount? GrossAmount { get; set; }
    public PayPalMoneyAmount? PaypalFee { get; set; }
    public PayPalMoneyAmount? NetAmount { get; set; }
}

internal sealed class PayPalCaptureRequest
{
    public bool FinalCapture { get; set; } = true;
}

internal sealed class PayPalReauthorizeRequest
{
    public PayPalMoneyAmount? Amount { get; set; }
}

internal sealed class PayPalRefundRequest
{
    public PayPalMoneyAmount? Amount { get; set; }
}

internal sealed class PayPalRefundResource
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyAmount? Amount { get; set; }
}

internal sealed class PayPalPaymentTokenRequest
{
    public PayPalVaultCustomer? Customer { get; set; }
    public PayPalPaymentSource PaymentSource { get; set; } = new();
}

internal sealed class PayPalVaultCustomer
{
    public string? Id { get; set; }
}

internal sealed class PayPalPaymentTokenResponse
{
    public string? Id { get; set; }
    public PayPalVaultedPaymentSource? PaymentSource { get; set; }
}

internal sealed class PayPalVaultedPaymentSource
{
    public PayPalVaultedCard? Card { get; set; }
}

internal sealed class PayPalVaultedCard
{
    public string? Name { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

internal sealed class PayPalSearchResponse
{
    public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
    public int Page { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

internal sealed class PayPalTransactionDetail
{
    public PayPalTransactionInfo? TransactionInfo { get; set; }
}

internal sealed class PayPalTransactionInfo
{
    public string? TransactionId { get; set; }
    public string? PaypalReferenceId { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public PayPalMoneyAmount? TransactionAmount { get; set; }
    public PayPalMoneyAmount? FeeAmount { get; set; }
    public DateTimeOffset? TransactionInitiationDate { get; set; }
}
