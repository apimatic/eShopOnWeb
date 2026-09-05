using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The card itself was refused. No money has moved and the order stays payable, so the shopper can
/// pay again with another card.
/// </summary>
public class CardDeclinedException : Exception
{
    public CardDeclinedException(string message) : base(message)
    {
    }
}
