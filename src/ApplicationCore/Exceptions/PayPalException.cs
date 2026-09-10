using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Represents an error returned by the PayPal REST API. Carries the machine-readable
/// error <see cref="Name"/> and first <see cref="Issue"/> so callers can branch on them
/// (for example, detecting an expired authorization during fulfilment).
/// </summary>
public class PayPalException : Exception
{
    public int StatusCode { get; }
    public string? Name { get; }
    public string? Issue { get; }
    public string? DebugId { get; }

    public PayPalException(int statusCode, string? name, string? issue, string? debugId, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        Issue = issue;
        DebugId = debugId;
    }
}
