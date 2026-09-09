using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A raw failure returned by the PayPal REST API. Exposes PayPal's debug id and the machine-readable
/// issue names so callers can decide how to react (for example, whether a capture failed because the
/// authorization must be renewed first).
/// </summary>
public class PayPalApiException : Exception
{
    public int HttpStatusCode { get; }
    public string? DebugId { get; }
    public string? Name { get; }
    public IReadOnlyList<string> IssueNames { get; }
    public string? RawBody { get; }

    public PayPalApiException(string message, int httpStatusCode, string? name,
        IReadOnlyList<string> issueNames, string? debugId, string? rawBody)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        Name = name;
        IssueNames = issueNames;
        DebugId = debugId;
        RawBody = rawBody;
    }

    /// <summary>True when PayPal reports the authorization is no longer directly capturable but may be
    /// renewed via re-authorization (honor period elapsed / authorization expired).</summary>
    public bool IndicatesReauthorizationNeeded()
    {
        var markers = new[] { "AUTHORIZATION_EXPIRED", "AUTH_EXPIRED", "REAUTHORIZATION_REQUIRED",
            "PAYER_AUTHORIZATION_EXPIRED" };
        return IssueNames.Any(i => markers.Contains(i, StringComparer.OrdinalIgnoreCase))
            || (Name is not null && markers.Contains(Name, StringComparer.OrdinalIgnoreCase));
    }
}
