using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointHelpers
{
    /// <summary>
    /// Resolves the authenticated caller to the Maxio customer profile. The JWT carries only
    /// the user name, so the application user is looked up to obtain its stable id, which is
    /// used as the Maxio customer reference. Returns null when the token has no usable
    /// identity (treated as unauthorized by the callers).
    /// </summary>
    public static async Task<MaxioCustomerProfile?> ResolveCustomerProfileAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var userName = httpContext.User.FindFirstValue(ClaimTypes.Name)
                       ?? httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var userManager = httpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? userName : user.Email;
        var (firstName, lastName) = DeriveName(email);
        return new MaxioCustomerProfile(user.Id, email, firstName, lastName);
    }

    public static SubscriptionPlanDto ToPlanDto(MaxioPlan plan)
    {
        return new SubscriptionPlanDto
        {
            Handle = plan.Handle,
            Name = plan.Name,
            Price = ToDollars(plan.PriceInCents),
            IntervalCount = plan.IntervalCount,
            Interval = plan.IntervalUnit
        };
    }

    public static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            Price = ToDollars(subscription.PriceInCents),
            Currency = subscription.Currency,
            IntervalCount = subscription.IntervalCount,
            Interval = subscription.IntervalUnit,
            State = subscription.State,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
        };
    }

    public static IResult ToErrorResult(MaxioException exception)
    {
        return Results.Json(
            new ErrorDetails
            {
                StatusCode = (int)exception.StatusCode,
                Message = exception.Message
            },
            statusCode: (int)exception.StatusCode);
    }

    public static IResult BadRequest(string message) => ToErrorResult(
        new MaxioApiException(HttpStatusCode.BadRequest, message, innerException: null));

    private static decimal? ToDollars(long? cents) => cents is { } c ? c / 100m : null;

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email;
        var at = email.IndexOf('@');
        if (at > 0)
        {
            localPart = email.Substring(0, at);
        }

        var parts = localPart.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return ("Member", "User");
        }

        var firstName = parts[0];
        var lastName = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "Member";
        return (firstName, lastName);
    }
}
