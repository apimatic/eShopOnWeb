using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// A call to the Maxio Advanced Billing API failed. Carries the HTTP status and the validation
/// messages Maxio returned so callers can react to specific failures without re-parsing the body.
/// </summary>
public class MaxioApiException : BillingProviderException
{
    public MaxioApiException(HttpStatusCode statusCode, string method, string path, IReadOnlyList<string> errors)
        : base(BuildMessage(statusCode, method, path, errors))
    {
        StatusCode = statusCode;
        Method = method;
        Path = path;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }

    public string Method { get; }

    public string Path { get; }

    /// <summary>Validation messages from Maxio's <c>{"errors": [...]}</c> body. Empty when the body carried none.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when the failure is Maxio rejecting a reference that is already taken, which is how a
    /// racing duplicate signup surfaces. The caller re-reads the existing record instead of failing.
    /// </summary>
    public bool IsDuplicateReference =>
        StatusCode == HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e => e.Contains("Reference", StringComparison.OrdinalIgnoreCase) &&
                        e.Contains("must be unique", StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(HttpStatusCode statusCode, string method, string path, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error detail returned";
        return $"Maxio API call {method} {path} failed with {(int)statusCode} {statusCode}: {detail}";
    }
}
