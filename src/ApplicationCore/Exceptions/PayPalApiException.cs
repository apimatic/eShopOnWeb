using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PayPalApiException : Exception
{
    public PayPalApiException(
        int statusCode,
        string? name,
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

    public int StatusCode { get; }
    public string? Name { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }

    public bool HasIssueContaining(params string[] fragments)
    {
        foreach (var issue in Issues)
        {
            foreach (var fragment in fragments)
            {
                if (issue.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        foreach (var fragment in fragments)
        {
            if (Message.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
