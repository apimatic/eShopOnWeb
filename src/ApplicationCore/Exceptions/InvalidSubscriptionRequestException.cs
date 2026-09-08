using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The request could not be honored because required information was missing or invalid.
/// </summary>
public class InvalidSubscriptionRequestException : Exception
{
    public InvalidSubscriptionRequestException(string message) : base(message)
    {
    }
}
