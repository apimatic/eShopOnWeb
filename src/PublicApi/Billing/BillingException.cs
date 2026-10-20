using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The one failure type <see cref="SubscriptionBillingService"/> raises. It carries the HTTP status the
/// caller should get, a caller-safe message and, for provider validation failures, Maxio's own
/// <c>errors</c> strings. It never carries provider URLs, raw bodies or credentials.
/// </summary>
public sealed class BillingException : Exception
{
    private BillingException(int statusCode, string title, string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Title = title;
        Errors = errors ?? Array.Empty<string>();
    }

    public int StatusCode { get; }
    public string Title { get; }
    public IReadOnlyList<string> Errors { get; }

    public static BillingException InvalidRequest(string message) =>
        new(StatusCodes.Status400BadRequest, "Invalid request", message);

    public static BillingException Unauthenticated() =>
        new(StatusCodes.Status401Unauthorized, "Unauthenticated", "The caller's identity could not be determined from the token.");

    public static BillingException InProgress() =>
        new(StatusCodes.Status409Conflict, "Request in progress", "An identical billing request for this account is already in progress. Retry shortly.");

    public static BillingException Rejected(string message, IReadOnlyList<string> errors) =>
        new(StatusCodes.Status422UnprocessableEntity, "Rejected by billing provider", message, errors);

    public static BillingException ProviderFailure(string message) =>
        new(StatusCodes.Status502BadGateway, "Billing provider error", message);

    public static BillingException ProviderUnavailable(string message) =>
        new(StatusCodes.Status503ServiceUnavailable, "Billing provider unavailable", message);

    public static BillingException Timeout() =>
        new(StatusCodes.Status504GatewayTimeout, "Billing provider timeout", "The billing provider did not answer in time. Retry shortly.");
}
