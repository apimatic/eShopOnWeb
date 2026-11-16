using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class FinixIdentityResult
{
    public string IdentityId { get; init; } = string.Empty;
}

public sealed class FinixCardResult
{
    public string PaymentInstrumentId { get; init; } = string.Empty;
    public string? Brand { get; init; }
    public string? LastFour { get; init; }
    public int? ExpirationMonth { get; init; }
    public int? ExpirationYear { get; init; }
    public string? CardType { get; init; }
    public string? Name { get; init; }
    public bool Enabled { get; init; } = true;
}

public sealed class FinixAuthorizationResult
{
    public string AuthorizationId { get; init; } = string.Empty;
    public string? State { get; init; }
    public long AmountInCents { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? TransferId { get; init; }
    public bool IsVoid { get; init; }
    public string? FailureCode { get; init; }
    public string? FailureMessage { get; init; }
}

public sealed class FinixCaptureResult
{
    public string TransferId { get; init; } = string.Empty;
    public string? TransferState { get; init; }
    public long AmountInCents { get; init; }
    public long FeeInCents { get; init; }
    public long NetAmountInCents { get; init; }
    public string? FailureCode { get; init; }
    public string? FailureMessage { get; init; }
}

public sealed class FinixRefundResult
{
    public string RefundTransferId { get; init; } = string.Empty;
    public string? State { get; init; }
    public long AmountInCents { get; init; }
}

public sealed class FinixTransactionRecord
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string? Type { get; init; }
    public string? State { get; init; }
    public long AmountInCents { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? ParentTransferId { get; init; }
}

public sealed class FinixFeeRecord
{
    public string Id { get; init; } = string.Empty;
    public long AmountInCents { get; init; }
    public string? Category { get; init; }
    public string? FeeType { get; init; }
    public string? LinkedTo { get; init; }
}

public interface IFinixPaymentGateway
{
    string Currency { get; }

    string MerchantId { get; }

    Task<FinixIdentityResult> CreateBuyerIdentityAsync(string buyerId, string? name,
        PaymentAddress? address, CancellationToken cancellationToken);

    Task<FinixCardResult> CreateCardAsync(string finixIdentityId, PaymentCardDetails card,
        CancellationToken cancellationToken);

    Task<FinixCardResult> GetCardAsync(string paymentInstrumentId, CancellationToken cancellationToken);

    Task DisableCardAsync(string paymentInstrumentId, CancellationToken cancellationToken);

    Task<FinixAuthorizationResult> AuthorizeAsync(string paymentInstrumentId, long amountInCents,
        string currency, string idempotencyKey, int orderId, string buyerId,
        CancellationToken cancellationToken);

    Task<FinixAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken);

    Task<FinixCaptureResult> CaptureAsync(string authorizationId, long amountInCents,
        string idempotencyKey, CancellationToken cancellationToken);

    Task<FinixCaptureResult> GetCaptureAsync(string captureTransferId,
        CancellationToken cancellationToken);

    Task<FinixAuthorizationResult> VoidAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixRefundResult> RefundAsync(string captureTransferId, long amountInCents,
        string idempotencyKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinixTransactionRecord>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinixFeeRecord>> ListFeesForTransferAsync(string transferId,
        CancellationToken cancellationToken);
}

/// <summary>
/// A normalized Finix failure. The gateway translates the SDK's typed errors into
/// this shape so callers can distinguish a decline from an operator-actionable
/// expired hold or a transient gateway problem.
/// </summary>
public sealed class FinixGatewayException : Exception
{
    public FinixGatewayException(string message, bool isDecline = false,
        bool isHoldNotCapturable = false, string? code = null, Exception? innerException = null)
        : base(message, innerException)
    {
        IsDecline = isDecline;
        IsHoldNotCapturable = isHoldNotCapturable;
        Code = code;
    }

    public bool IsDecline { get; }

    public bool IsHoldNotCapturable { get; }

    public string? Code { get; }
}
