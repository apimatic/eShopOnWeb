using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Exactly what the payment provider sent back for one call — the raw body, verbatim — kept with the order so
/// support can see every field, including ones this build does not know about.
/// </summary>
public class PaymentProviderResponse : BaseEntity
{
    public const string PaymentOperation = "payment";
    public const string RefundOperation = "refund";

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentProviderResponse() {}

    internal PaymentProviderResponse(int orderId, string operation, int? paymentAttemptNumber, int? refundSequence,
        DateTimeOffset receivedAt, int? httpStatus, string? body, string? note)
    {
        OrderId = orderId;
        Operation = operation;
        PaymentAttemptNumber = paymentAttemptNumber;
        RefundSequence = refundSequence;
        ReceivedAt = receivedAt;
        HttpStatus = httpStatus;
        Body = body;
        Note = note;
    }

    public int OrderId { get; private set; }

    /// <summary><see cref="PaymentOperation"/> or <see cref="RefundOperation"/>.</summary>
    public string Operation { get; private set; }

    public int? PaymentAttemptNumber { get; private set; }
    public int? RefundSequence { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Null when no response arrived (see <see cref="Note"/>).</summary>
    public int? HttpStatus { get; private set; }

    /// <summary>The response body exactly as received.</summary>
    public string? Body { get; private set; }

    /// <summary>What happened when there is no body to show, for example "no response within 10 s".</summary>
    public string? Note { get; private set; }
}
