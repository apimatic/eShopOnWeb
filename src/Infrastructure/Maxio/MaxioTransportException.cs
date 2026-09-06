using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The Maxio API could not be reached, or did not answer in time. Distinct from
/// <see cref="MaxioApiException"/>, which represents an answer we did receive.
/// </summary>
public sealed class MaxioTransportException : Exception
{
    public MaxioTransportException(string operationId, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        OperationId = operationId;
    }

    /// <summary>The specification <c>operationId</c> of the call that failed.</summary>
    public string OperationId { get; }

    /// <summary>True when the call was abandoned because it exceeded the configured timeout.</summary>
    public bool IsTimeout { get; init; }
}
