using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The one failure type the subscription endpoints translate into an HTTP response. It carries a
/// caller-safe message and the status the caller should see - never an SDK message or type name.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(int statusCode, string message, IReadOnlyList<string>? providerErrors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderErrors = providerErrors ?? Array.Empty<string>();
    }

    /// <summary>The HTTP status the caller receives.</summary>
    public int StatusCode { get; }

    /// <summary>Provider validation messages that are safe to show the caller (e.g. a 422 refusal).</summary>
    public IReadOnlyList<string> ProviderErrors { get; }
}

/// <summary>
/// A write the provider definitely refused (an error status with a readable or unreadable body). Nothing was
/// created, so the caller's claim can be released.
/// </summary>
public sealed class MaxioWriteRejectedException : Exception
{
    public MaxioWriteRejectedException(int providerStatus, IReadOnlyList<string> providerErrors, Exception innerException)
        : base($"Maxio refused the write with HTTP {providerStatus}.", innerException)
    {
        ProviderStatus = providerStatus;
        ProviderErrors = providerErrors;
    }

    public int ProviderStatus { get; }
    public IReadOnlyList<string> ProviderErrors { get; }
}

/// <summary>
/// A write whose outcome is unknown: the connection failed, the call budget ran out, the provider answered
/// 5xx, or a 2xx body could not be read. The provider may already have acted, so the caller must re-read
/// provider state before reporting anything.
/// </summary>
public sealed class MaxioWriteOutcomeUnknownException : Exception
{
    public MaxioWriteOutcomeUnknownException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
