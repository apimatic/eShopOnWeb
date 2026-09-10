using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment action the caller requested is not valid for the order's current state — e.g. paying an
/// order that is already paid, cancelling one that is already captured, or refunding beyond the captured
/// amount. Distinct from a provider failure (<see cref="PaymentGatewayException"/>); maps to a 4xx.
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message) { }
}
