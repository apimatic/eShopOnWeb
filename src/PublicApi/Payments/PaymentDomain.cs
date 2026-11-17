using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public enum OrderPaymentStatus
{
    AwaitingPayment,
    Authorized,
    Fulfilled,
    Canceled,
    PaymentFailed,
    PaymentUnknown
}

public sealed class OrderPaymentRecord
{
    public int OrderId { get; init; }
    public required string BuyerId { get; init; }
    public required Order Order { get; init; }
    public required string PaymentReference { get; init; }
    public decimal TotalAmount { get; init; }
    public long TotalAmountCents { get; init; }
    public required string Currency { get; init; }
    public OrderPaymentStatus Status { get; set; } = OrderPaymentStatus.AwaitingPayment;
    public string? PaymentMethodId { get; set; }

    public string? AuthorizationId { get; set; }
    public string? HoldStatus { get; set; }
    public long? HeldAmountCents { get; set; }
    public DateTimeOffset? HoldExpiresAt { get; set; }

    public string? CaptureTransferId { get; set; }
    public string? CaptureStatus { get; set; }
    public long? CapturedAmountCents { get; set; }
    public decimal? FinixFeeCents { get; set; }
    public decimal? NetProceedsCents { get; set; }
    public bool FeesFinalized { get; set; }

    public long RefundedAmountCents { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? OperatorExplanation { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<RefundRecord> Refunds { get; } = new();
}

public sealed class RefundRecord
{
    public required string RefundId { get; init; }
    public required string IdempotencyKey { get; init; }
    public long AmountCents { get; init; }
    public string Status { get; set; } = "PENDING";
    public string? FinixRefundId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SavedCardRecord
{
    public required string PaymentMethodId { get; init; }
    public required string BuyerId { get; init; }
    public required string FinixPaymentInstrumentId { get; init; }
    public string? Brand { get; init; }
    public string? Last4 { get; init; }
    public int ExpirationMonth { get; init; }
    public int ExpirationYear { get; init; }
    public string? Name { get; init; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DisabledAt { get; set; }
}

public sealed class ClaimEntry
{
    public required string Key { get; init; }
    public bool Completed { get; set; }
    public int StatusCode { get; set; }
    public object? Body { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed record ServiceResult(int StatusCode, object? Body = null)
{
    public static ServiceResult Ok(object? body = null) => new(200, body);
    public static ServiceResult Created(object? body = null) => new(201, body);
    public static ServiceResult NoContent() => new(204);
    public static ServiceResult BadRequest(string error, string? explanation = null) =>
        new(400, new ApiErrorBody(error, explanation));
    public static ServiceResult NotFound(string error = "NOT_FOUND", string? explanation = null) =>
        new(404, new ApiErrorBody(error, explanation));
    public static ServiceResult Conflict(string error, string? explanation = null) =>
        new(409, new ApiErrorBody(error, explanation));
    public static ServiceResult Failure(int status, string error, string? explanation = null) =>
        new(status, new ApiErrorBody(error, explanation));
}

public sealed record ApiErrorBody(string Error, string? Explanation = null);
