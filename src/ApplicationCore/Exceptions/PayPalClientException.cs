using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PayPalClientException : Exception
{
    public PayPalClientException(
        string message,
        int statusCode = 0,
        string? issue = null,
        string? debugId = null,
        string? name = null)
        : base(message)
    {
        StatusCode = statusCode;
        Issue = issue;
        DebugId = debugId;
        Name = name;
    }

    public int StatusCode { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
    public string? Name { get; }

    public bool IsExpiredAuthorization =>
        string.Equals(Issue, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
        || MessageContains("expired authorization");

    public bool IsAlreadyVoided =>
        string.Equals(Issue, "AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Issue, "AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase)
        || MessageContains("already voided");

    public bool MustVoidOriginalAuthorization =>
        string.Equals(Issue, "CANNOT_BE_VOIDED", StringComparison.OrdinalIgnoreCase)
        || MessageContains("void the original parent");

    public bool CannotReauthorize =>
        string.Equals(Issue, "AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Issue, "MAX_NUMBER_OF_REAUTHORIZATION_EXCEEDED", StringComparison.OrdinalIgnoreCase)
        || MessageContains("cannot be reauthorized")
        || MessageContains("must create an authorized payment");

    public bool RequiresPayerAction =>
        string.Equals(Issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
        || MessageContains("payer_action_required");

    private bool MessageContains(string fragment) =>
        Message.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
