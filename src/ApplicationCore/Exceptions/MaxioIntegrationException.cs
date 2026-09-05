using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the Maxio billing provider cannot be reached or fails in a way the caller cannot
/// act on (as opposed to <see cref="MaxioValidationException"/>, which the caller can fix).
/// </summary>
public class MaxioIntegrationException : Exception
{
    public MaxioIntegrationException(string message) : base(message)
    {
    }

    public MaxioIntegrationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
