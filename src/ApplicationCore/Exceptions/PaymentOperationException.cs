using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised for payment lifecycle problems that an operator or shopper can act on, phrased in terms
/// they can understand (e.g. an authorization that can no longer be renewed before fulfilment).
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(string message) : base(message)
    {
    }
}
