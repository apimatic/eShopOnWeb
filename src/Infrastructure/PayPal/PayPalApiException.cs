using System;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Raised when PayPal returns a non-success HTTP status. Carries the PayPal error name/issue so
/// callers can react to specific conditions (e.g. an expired authorization during capture).
/// </summary>
public class PayPalApiException : PaymentException
{
    public HttpStatusCode StatusCode { get; }
    public string PayPalName { get; }
    public string Issue { get; }

    public PayPalApiException(string message, HttpStatusCode statusCode, string payPalName, string issue)
        : base(message)
    {
        StatusCode = statusCode;
        PayPalName = payPalName;
        Issue = issue;
    }

    /// <summary>True when the error indicates the authorization hold has expired and must be renewed.</summary>
    public bool IndicatesExpiredAuthorization =>
        Issue.Equals("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
        || PayPalName.Equals("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase);

    public override bool IsDuplicateRequest =>
        Issue.Equals("DUPLICATE_REQUEST_ID", StringComparison.OrdinalIgnoreCase)
        || PayPalName.Equals("DUPLICATE_REQUEST_ID", StringComparison.OrdinalIgnoreCase);
}
