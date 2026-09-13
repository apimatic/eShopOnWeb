using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class Register
{
    private static async Task<(bool Success, ClaimsPrincipal? Principal)> AuthenticateJwtAsync(HttpContext httpContext)
    {
        var authResult = await httpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        return (authResult.Succeeded, authResult.Principal);
    }

    public static async Task<IResult> CreateSubscriptionHandler(
        CreateSubscriptionRequest request,
        IMaxioSubscriptionService subscriptionService,
        HttpContext httpContext)
    {
        var (success, principal) = await AuthenticateJwtAsync(httpContext);
        if (!success || principal == null)
            return Results.Unauthorized();

        httpContext.User = principal;

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var userEmail = principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("preferred_username")
            ?? $"{userId}@eshoponweb.local";
        var firstName = principal.FindFirstValue(ClaimTypes.GivenName) ?? "eShop";
        var lastName = principal.FindFirstValue(ClaimTypes.Surname) ?? "Customer";

        var subscription = await subscriptionService.SubscribeAsync(
            userId, request.ProductHandle, userEmail, firstName, lastName);

        return Results.Ok(new CreateSubscriptionResponse
        {
            Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State,
                PlanName = subscription.Product?.Name ?? string.Empty,
                PlanHandle = subscription.Product?.Handle ?? string.Empty,
                PriceInDollars = subscription.ProductPriceInCents / 100m,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
                CanceledAt = subscription.CanceledAt
            }
        });
    }

    public static async Task<IResult> MySubscriptionsHandler(
        IMaxioSubscriptionService subscriptionService,
        HttpContext httpContext)
    {
        var (success, principal) = await AuthenticateJwtAsync(httpContext);
        if (!success || principal == null)
            return Results.Unauthorized();

        httpContext.User = principal;

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var subscriptions = await subscriptionService.GetMySubscriptionsAsync(userId);
        return Results.Ok(new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                PlanName = s.Product?.Name ?? string.Empty,
                PlanHandle = s.Product?.Handle ?? string.Empty,
                PriceInDollars = s.ProductPriceInCents / 100m,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
                ActivatedAt = s.ActivatedAt,
                CanceledAt = s.CanceledAt
            }).ToList()
        });
    }
}
