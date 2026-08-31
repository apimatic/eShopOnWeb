using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The payment provider declined the card or capture (a successful API call whose
/// status reports a decline).
/// </summary>
public class PaymentDeclinedException : Exception
{
    public PaymentDeclinedException(string message) : base(message)
    {
    }
}
