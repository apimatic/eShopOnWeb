using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// An idempotency claim for a one-per-order payment write (authorize / capture / void). Persisted with a
/// unique index on (OrderPaymentId, OperationType) <em>before</em> the PayPal call, so a concurrent
/// double-submit fails to insert the second claim and never reaches the provider twice.
/// </summary>
public class PaymentOperation : BaseEntity, IAggregateRoot
{
    public const string Authorize = "AUTHORIZE";
    public const string Capture = "CAPTURE";
    public const string Void = "VOID";

    public int OrderPaymentId { get; private set; }
    public string OperationType { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentOperation() { }
#pragma warning restore CS8618

    public PaymentOperation(int orderPaymentId, string operationType)
    {
        OrderPaymentId = orderPaymentId;
        OperationType = operationType;
    }
}
