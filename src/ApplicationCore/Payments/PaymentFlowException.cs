using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A payment-flow request was invalid given the current state (unknown order, wrong state, over-refund,
/// missing payment source, and the like). Carries a caller-safe message and maps to a client 4xx.
/// </summary>
public class PaymentFlowException : Exception
{
    public PaymentFlowException(string message) : base(message)
    {
    }
}
