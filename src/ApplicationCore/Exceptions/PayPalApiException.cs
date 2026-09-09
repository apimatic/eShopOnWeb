using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal REST call fails. Carries PayPal's HTTP status and debug id so the failure
/// can be surfaced and correlated. <see cref="RequiresBuyerAction"/> flags the one case this
/// integration deliberately does not build around: PayPal asking for a browser approval / 3DS
/// challenge (per the task, that is reported, not worked around).
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(string message, int? statusCode = null, string? debugId = null,
        bool requiresBuyerAction = false, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        RequiresBuyerAction = requiresBuyerAction;
    }

    public int? StatusCode { get; }
    public string? DebugId { get; }
    public bool RequiresBuyerAction { get; }
}
