using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The caller supplied input the subscription module rejects before any provider call is made — for
/// example a zero or negative usage quantity, or a missing plan handle.
/// </summary>
public class InvalidBillingRequestException : Exception
{
    public InvalidBillingRequestException(string message) : base(message)
    {
    }
}
