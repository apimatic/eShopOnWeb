using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A PayPal API call failed. Carries PayPal's error name/issue so callers
/// (and operators) can see what PayPal reported.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, string? payPalErrorName = null, string? payPalIssue = null)
        : base(message)
    {
        PayPalErrorName = payPalErrorName;
        PayPalIssue = payPalIssue;
    }

    public string? PayPalErrorName { get; }
    public string? PayPalIssue { get; }
    public HttpStatusCode? HttpStatusCode { get; set; }
}

/// <summary>
/// The payment was declined or could not be authorized.
/// </summary>
public class PaymentDeclinedException : PaymentGatewayException
{
    public PaymentDeclinedException(string message, string? payPalErrorName = null, string? payPalIssue = null)
        : base(message, payPalErrorName, payPalIssue)
    {
    }
}

/// <summary>
/// The authorization went stale and PayPal would not renew it. The operator
/// must ask the shopper to pay again.
/// </summary>
public class AuthorizationCannotBeRenewedException : PaymentGatewayException
{
    public AuthorizationCannotBeRenewedException(string message, string? payPalErrorName = null, string? payPalIssue = null)
        : base(message, payPalErrorName, payPalIssue)
    {
    }
}

/// <summary>
/// PayPal answered with a challenge that requires the shopper to approve in a
/// browser, which this integration does not support.
/// </summary>
public class PayerActionRequiredException : PaymentGatewayException
{
    public PayerActionRequiredException(string message)
        : base(message)
    {
    }
}
