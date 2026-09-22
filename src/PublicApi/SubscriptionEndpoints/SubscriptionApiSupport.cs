using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared helpers for the subscription endpoints: deriving the billing customer from the JWT
/// identity, projecting domain results to DTOs, and mapping billing failures to HTTP results.
/// </summary>
internal static class SubscriptionApiSupport
{
    /// <summary>
    /// Builds the billing customer from the authenticated identity. In eShopOnWeb the JWT
    /// <c>Name</c> claim is the user's email/username; the reference is a stable, deterministic key
    /// derived from it so the same Maxio customer is found again across restarts.
    /// </summary>
    public static BillingCustomer? BuildCustomer(ClaimsPrincipal? principal)
    {
        var username = principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
            return null;

        var email = username!;
        var atIndex = email.IndexOf('@');
        var firstName = atIndex > 0 ? email.Substring(0, atIndex) : email;

        return new BillingCustomer
        {
            Reference = "eshop-user-" + Sanitize(username!),
            Email = email,
            FirstName = string.IsNullOrWhiteSpace(firstName) ? "eShop" : firstName,
            LastName = "eShopOnWeb"
        };
    }

    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(SubscriptionDetails details) => new()
    {
        Id = details.Id,
        Reference = details.Reference,
        PlanHandle = details.PlanHandle,
        PlanName = details.PlanName,
        PriceInCents = details.PriceInCents,
        Currency = details.Currency,
        State = details.State,
        NextBillingDate = details.NextBillingDate,
        CreatedAt = details.CreatedAt
    };

    /// <summary>Maps a billing failure to a caller-facing HTTP result (never leaks provider detail).</summary>
    public static IResult ToErrorResult(BillingException ex) => ex.Kind switch
    {
        BillingErrorKind.InvalidRequest => Results.BadRequest(new ErrorMessage(ex.Message)),
        BillingErrorKind.NotFound => Results.NotFound(new ErrorMessage(ex.Message)),
        BillingErrorKind.ProviderUnavailable => Results.Json(new ErrorMessage(ex.Message),
            statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.Json(new ErrorMessage(ex.Message), statusCode: StatusCodes.Status502BadGateway)
    };

    public static IResult Unauthenticated() =>
        Results.Json(new ErrorMessage("The request identity could not be determined."),
            statusCode: StatusCodes.Status401Unauthorized);

    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-');
        return sb.ToString();
    }

    public sealed record ErrorMessage(string Message);
}
