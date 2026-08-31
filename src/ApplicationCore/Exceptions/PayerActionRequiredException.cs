using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The provider answered a card payment with a challenge requiring browser approval
/// (e.g. 3-D Secure). This integration does not support an approval round-trip.
/// </summary>
public class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException(string message) : base(message)
    {
    }
}
