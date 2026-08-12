using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<PaymentAuthorizationResult> AuthorizePaymentAsync(Order order, PaymentDetails payment, string idempotencyKey, CancellationToken ct = default);
    Task<PaymentCaptureResult> CapturePaymentAsync(PaymentReference paymentRef, CancellationToken ct = default);
    Task<PaymentVoidResult> VoidPaymentAsync(PaymentReference paymentRef, CancellationToken ct = default);
    Task<PaymentRefundResult> RefundPaymentAsync(PaymentReference paymentRef, decimal refundAmount, string idempotencyKey, CancellationToken ct = default);
    Task<SavedCardDetails> SaveCardAsync(string buyerId, string cardToken, string? cardholderName, CancellationToken ct = default);
    Task DeleteSavedCardAsync(string payPalPaymentTokenId, CancellationToken ct = default);
    Task<IReadOnlyList<SavedCardDetails>> ListSavedCardsAsync(string buyerId, CancellationToken ct = default);
    Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(DateTime fromDate, DateTime toDate, CancellationToken ct = default);
}

public class PaymentDetails
{
    public string? SavedPaymentMethodId { get; set; }
    public CardDetails? CardDetails { get; set; }
}

public class CardDetails
{
    public string CardNumber { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string Cvv { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
}

public class PaymentAuthorizationResult
{
    public bool Success { get; set; }
    public string? OrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PaymentCaptureResult
{
    public bool Success { get; set; }
    public string? CaptureId { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal PaypalFee { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PaymentVoidResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PaymentRefundResult
{
    public bool Success { get; set; }
    public string? RefundId { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SavedCardDetails
{
    public string Id { get; set; } = string.Empty;
    public string? LastFourDigits { get; set; }
    public string? Brand { get; set; }
    public string? CardholderName { get; set; }
    public string? ExpiryDate { get; set; }
}

public class TransactionRecord
{
    public string TransactionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? InvoiceId { get; set; }
}
