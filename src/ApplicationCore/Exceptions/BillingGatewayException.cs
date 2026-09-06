using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system was reached but rejected or failed the request. <see cref="UpstreamStatusCode"/>
/// and <see cref="Errors"/> carry what it told us, so the failure can be surfaced without guesswork.
/// </summary>
public class BillingGatewayException : BillingException
{
    public BillingGatewayException(string message, int? upstreamStatusCode = null, IEnumerable<string>? errors = null)
        : base(message)
    {
        UpstreamStatusCode = upstreamStatusCode;
        Errors = errors?.ToArray() ?? Array.Empty<string>();
    }

    public BillingGatewayException(string message, Exception innerException) : base(message, innerException)
    {
        Errors = Array.Empty<string>();
    }

    public int? UpstreamStatusCode { get; }

    public IReadOnlyList<string> Errors { get; }
}
