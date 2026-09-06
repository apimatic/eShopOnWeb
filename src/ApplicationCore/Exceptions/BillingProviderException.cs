using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider answered, but not with success. Carries the provider's own error messages so
/// they can be relayed verbatim - Maxio returns an <c>errors</c> array (see
/// <c>components/schemas/errors/Error-List-Response.yaml</c>) that is genuinely useful to the caller.
/// </summary>
public class BillingProviderException : BillingException
{
    public BillingProviderException(string message, int? statusCode = null, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(BuildMessage(message, errors), innerException!)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>HTTP status the provider responded with, when the call reached it at all.</summary>
    public int? StatusCode { get; }

    /// <summary>Provider-supplied error messages, in the order the provider returned them.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when the provider rejected what we sent (a 4xx). Those are relayed to the caller as a
    /// client error; anything else is an upstream fault and becomes <c>502 Bad Gateway</c>.
    /// </summary>
    public bool IsRequestRejected => StatusCode is >= 400 and < 500;

    private static string BuildMessage(string message, IReadOnlyList<string>? errors) =>
        errors is { Count: > 0 } ? $"{message} {string.Join(" ", errors)}" : message;
}
