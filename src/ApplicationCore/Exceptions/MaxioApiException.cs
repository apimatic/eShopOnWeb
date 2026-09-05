using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a call to the Maxio Advanced Billing API fails unexpectedly.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(string message) : base(message)
    {
    }
}
