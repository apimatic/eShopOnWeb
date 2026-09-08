using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared plumbing for the subscription endpoints: resolves the JWT-authenticated shopper into a
/// Maxio subscriber profile, maps integration models to API DTOs, and maps Maxio failures to
/// HTTP results without leaking upstream detail beyond actionable messages.
/// </summary>
internal static class SubscriptionEndpointSupport
{
    public const string SUBSCRIBER_LAST_NAME = "User";

    public static async Task<SubscriberProfile?> ResolveSubscriberAsync(ClaimsPrincipal user, UserManager<ApplicationUser> userManager)
    {
        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var appUser = await userManager.FindByNameAsync(userName);
        if (appUser is null)
        {
            return null;
        }

        var email = appUser.Email ?? userName;
        var firstName = email.Contains('@') ? email[..email.IndexOf('@')] : email;

        return new SubscriberProfile
        {
            Reference = MaxioSubscriptionService.CustomerReferenceFor(appUser.Id),
            FirstName = firstName,
            LastName = SUBSCRIBER_LAST_NAME,
            Email = email,
        };
    }

    public static SubscriptionPlanDto ToDto(MaxioPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
    };

    public static MySubscriptionDto ToDto(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CreatedDate = subscription.CreatedAt,
    };

    public static IResult MapMaxioFailure(MaxioApiException ex)
    {
        var upstreamIsClientError = (int)ex.StatusCode is >= 400 and <= 499 && ex.StatusCode != HttpStatusCode.TooManyRequests;
        var statusCode = upstreamIsClientError ? (int)ex.StatusCode : (int)HttpStatusCode.BadGateway;
        var message = upstreamIsClientError
            ? string.Join("; ", ex.Errors.DefaultIfEmpty("The billing provider rejected the request."))
            : "The billing provider is temporarily unavailable. Please try again shortly.";

        return Results.Json(new ErrorDetails { StatusCode = statusCode, Message = message }, statusCode: statusCode);
    }

    public static IResult BadRequest(string message) =>
        Results.Json(new ErrorDetails { StatusCode = StatusCodes.Status400BadRequest, Message = message }, statusCode: StatusCodes.Status400BadRequest);
}
