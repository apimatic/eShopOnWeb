using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment/fulfilment operation is not valid against the current order state — for
/// example refunding beyond the captured amount, or cancelling an order that is already fulfilled.
/// Represents a caller-actionable conflict (mapped to HTTP 409/422 at the API boundary), distinct
/// from a transport or provider failure (see <see cref="PaymentGatewayException"/>).
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(string message) : base(message)
    {
    }

    public PaymentOperationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
