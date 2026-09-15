using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class PaypalTokenResponse
{
    public string? AccessToken { get; set; }
    public string? TokenType { get; set; }
    public int ExpiresIn { get; set; }
}

internal sealed class PaypalErrorResponse
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public List<PaypalErrorDetail>? Details { get; set; }
}

internal sealed class PaypalErrorDetail
{
    public string? Field { get; set; }
    public string? Issue { get; set; }
    public string? Description { get; set; }
}

internal sealed class PaypalOrderResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public List<PaypalPurchaseUnit>? PurchaseUnits { get; set; }
    public List<PaypalLink>? Links { get; set; }
}

internal sealed class PaypalPurchaseUnit
{
    public PaypalPurchasePayments? Payments { get; set; }
}

internal sealed class PaypalPurchasePayments
{
    public List<PaypalAuthorizationResource>? Authorizations { get; set; }
    public List<PaypalCaptureResource>? Captures { get; set; }
}

internal sealed class PaypalAuthorizationResource
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PaypalMoney? Amount { get; set; }
    public System.DateTimeOffset? ExpirationTime { get; set; }
    public System.DateTimeOffset? CreateTime { get; set; }
}

internal sealed class PaypalCaptureResource
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PaypalMoney? Amount { get; set; }
    public PaypalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal sealed class PaypalSellerReceivableBreakdown
{
    public PaypalMoney? GrossAmount { get; set; }
    public PaypalMoney? PaypalFee { get; set; }
    public PaypalMoney? NetAmount { get; set; }
}

internal sealed class PaypalRefundResource
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PaypalMoney? Amount { get; set; }
}

internal sealed class PaypalMoney
{
    public string? CurrencyCode { get; set; }
    public string? Value { get; set; }
}

internal sealed class PaypalLink
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}

internal sealed class PaypalSetupTokenResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PaypalCustomer? Customer { get; set; }
    public PaypalVaultPaymentSource? PaymentSource { get; set; }
    public List<PaypalLink>? Links { get; set; }
}

internal sealed class PaypalPaymentTokenResponse
{
    public string? Id { get; set; }
    public PaypalCustomer? Customer { get; set; }
    public PaypalVaultPaymentSource? PaymentSource { get; set; }
}

internal sealed class PaypalCustomer
{
    public string? Id { get; set; }
}

internal sealed class PaypalVaultPaymentSource
{
    public PaypalVaultCard? Card { get; set; }
}

internal sealed class PaypalVaultCard
{
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? Name { get; set; }
}

internal sealed class PaypalTransactionSearchResponse
{
    public List<PaypalTransactionDetail>? TransactionDetails { get; set; }
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public int Page { get; set; }
}

internal sealed class PaypalTransactionDetail
{
    public PaypalTransactionInfo? TransactionInfo { get; set; }
}

internal sealed class PaypalTransactionInfo
{
    public string? TransactionId { get; set; }
    public string? PaypalReferenceId { get; set; }
    public string? PaypalReferenceIdType { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public System.DateTimeOffset? TransactionInitiationDate { get; set; }
    public PaypalMoney? TransactionAmount { get; set; }
    public PaypalMoney? FeeAmount { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
}
