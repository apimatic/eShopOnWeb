using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raised when a messaging-provider call fails. The message is deliberately free of any personal data
/// (no destination number is ever included) so it is safe to log or surface.
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message) : base(message) { }

    public SmsProviderException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>The provider's error code, when it returned one.</summary>
    public int? ProviderErrorCode { get; init; }
}
