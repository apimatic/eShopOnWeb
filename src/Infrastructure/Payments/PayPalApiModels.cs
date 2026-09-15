using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class PayPalTokenResponse
{
    public string? AccessToken { get; set; }
    public string? TokenType { get; set; }
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
    public string? Issue { get; set; }
    public string? Description { get; set; }
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
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public PayPalMoney Amount { get; set; } = new();
}

internal sealed class PayPalMoney
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal sealed class PayPalPaymentSource
{
    public PayPalCardSource? Card { get; set; }
}

internal sealed class PayPalCardSource
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? VaultId { get; set; }
    public PayPalAddress? BillingAddress { get; set; }
}

internal sealed class PayPalAddress
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

internal sealed class PayPalOrderResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public string? Intent { get; set; }
    public List<PayPalPurchaseUnitResponse>? PurchaseUnits { get; set; }
    public List<PayPalLink>? Links { get; set; }
}

internal sealed class PayPalPurchaseUnitResponse
{
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public PayPalMoney? Amount { get; set; }
    public PayPalPaymentsContainer? Payments { get; set; }
}

internal sealed class PayPalPaymentsContainer
{
    public List<PayPalAuthorization>? Authorizations { get; set; }
    public List<PayPalCapture>? Captures { get; set; }
}

internal sealed class PayPalAuthorization
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoney? Amount { get; set; }
    public string? CreateTime { get; set; }
    public string? UpdateTime { get; set; }
    public string? ExpirationTime { get; set; }
}

internal sealed class PayPalCapture
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoney? Amount { get; set; }
    public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
    public string? CreateTime { get; set; }
}

internal sealed class PayPalSellerReceivableBreakdown
{
    public PayPalMoney? GrossAmount { get; set; }
    public PayPalMoney? PaypalFee { get; set; }
    public PayPalMoney? NetAmount { get; set; }
}

internal sealed class PayPalRefund
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoney? Amount { get; set; }
}

internal sealed class PayPalLink
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}

internal sealed class PayPalSetupTokenRequest
{
    public PayPalVaultCustomer? Customer { get; set; }
    public PayPalSetupPaymentSource PaymentSource { get; set; } = new();
}

internal sealed class PayPalSetupPaymentSource
{
    public PayPalSetupCard Card { get; set; } = new();
}

internal sealed class PayPalSetupCard
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public PayPalAddress? BillingAddress { get; set; }
}

internal sealed class PayPalVaultCustomer
{
    public string? Id { get; set; }
}

internal sealed class PayPalSetupTokenResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalVaultCustomer? Customer { get; set; }
    public PayPalVaultedPaymentSource? PaymentSource { get; set; }
    public List<PayPalLink>? Links { get; set; }
}

internal sealed class PayPalPaymentTokenRequest
{
    public PayPalTokenPaymentSource PaymentSource { get; set; } = new();
}

internal sealed class PayPalTokenPaymentSource
{
    public PayPalTokenRef Token { get; set; } = new();
}

internal sealed class PayPalTokenRef
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "SETUP_TOKEN";
}

internal sealed class PayPalPaymentTokenResponse
{
    public string? Id { get; set; }
    public PayPalVaultCustomer? Customer { get; set; }
    public PayPalVaultedPaymentSource? PaymentSource { get; set; }
}

internal sealed class PayPalVaultedPaymentSource
{
    public PayPalVaultedCard? Card { get; set; }
}

internal sealed class PayPalVaultedCard
{
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? Name { get; set; }
}

internal sealed class PayPalTransactionSearchResponse
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
    public string? PaypalReferenceIdType { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionInitiationDate { get; set; }
    public string? TransactionStatus { get; set; }
    public PayPalMoney? TransactionAmount { get; set; }
    public PayPalMoney? FeeAmount { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
}

internal sealed class PayPalCaptureRequest
{
    public PayPalMoney? Amount { get; set; }
    public bool FinalCapture { get; set; } = true;
    public string? InvoiceId { get; set; }
}

internal sealed class PayPalRefundRequest
{
    public PayPalMoney? Amount { get; set; }
}

internal sealed class PayPalReauthorizeRequest
{
    public PayPalMoney? Amount { get; set; }
}
