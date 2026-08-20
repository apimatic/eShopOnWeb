using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PayPalGatewayException : OrderPaymentException
{
    public string? PayPalName { get; }
    public string? DebugId { get; }
    public string? Issue { get; }

    public PayPalGatewayException(int statusCode, string message, string? paypalName, string? debugId, string? issue)
        : base(statusCode, message)
    {
        PayPalName = paypalName;
        DebugId = debugId;
        Issue = issue;
    }

    public bool HasIssue(string issue) =>
        string.Equals(Issue, issue, StringComparison.OrdinalIgnoreCase)
        || string.Equals(PayPalName, issue, StringComparison.OrdinalIgnoreCase);

    public bool IsExpiredAuthorization()
    {
        return HasIssue("AUTHORIZATION_EXPIRED")
            || HasIssue("AUTHORIZATION_VOIDED")
            || HasIssue("EXPIRED_TRANSACTION")
            || HasIssue("AUTH_CAPTURE_NOT_ALLOWED")
            || (Issue?.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ?? false)
            || (PayPalName?.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    public bool CannotReauthorize()
    {
        return HasIssue("INVALID_RESOURCE_ID")
            || HasIssue("MAX_NUMBER_OF_REAUTHORIZATION_ATTEMPTS_EXCEEDED")
            || HasIssue("REAUTHORIZATION_NOT_ALLOWED")
            || HasIssue("AUTHORIZATION_VOIDED")
            || HasIssue("AUTHORIZATION_ALREADY_CAPTURED")
            || HasIssue("CANNOT_BE_REAUTHORIZED")
            || (Issue?.Contains("REAUTHORIZ", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
