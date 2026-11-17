using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A durable claim taken before an operator action (fulfil/cancel) reaches Finix. The composite
/// key rejects a concurrent second action for the same order.
/// </summary>
public class ActionClaim
{
    public int OrderId { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
