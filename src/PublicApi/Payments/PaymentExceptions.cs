using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class PaymentApiException : Exception
{
    public PaymentApiException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

internal sealed class PayPalApiException : Exception
{
    public PayPalApiException(
        HttpStatusCode statusCode,
        string name,
        string message,
        string? debugId,
        IReadOnlyList<string> issues)
        : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        DebugId = debugId;
        Issues = issues;
    }

    public HttpStatusCode StatusCode { get; }
    public string Name { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }

    public bool HasIssue(string issue) => Issues.Contains(issue, StringComparer.OrdinalIgnoreCase);
}
