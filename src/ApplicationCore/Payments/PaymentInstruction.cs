using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// How a shopper wants to pay: either raw card details for a one-off payment, or the id of one of
/// their saved cards. Exactly one must be supplied.
/// </summary>
public class PaymentInstruction
{
    public CardDetails? Card { get; init; }
    public int? SavedCardId { get; init; }

    public bool IsValid => (Card is not null) ^ SavedCardId.HasValue;
}

/// <summary>A snapshot of an order together with its payment (if any) for building responses.</summary>
public record OrderPaymentState(Order Order, Payment? Payment);
