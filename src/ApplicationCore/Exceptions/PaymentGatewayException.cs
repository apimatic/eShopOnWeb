using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The payment provider rejected a call. Carries the provider's HTTP status code,
/// error name and issue details from its standard error model.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(int statusCode, string? errorName, string message, IReadOnlyList<string>? issues = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        Issues = issues ?? Array.Empty<string>();
    }

    public int StatusCode { get; }
    public string? ErrorName { get; }
    public IReadOnlyList<string> Issues { get; }

    public bool IsUnprocessable => StatusCode == 422;
}
