using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointSupport
{
    public static async Task<BillingUser?> GetBillingUserAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var applicationUser = await userManager.FindByNameAsync(userName);
        if (applicationUser is null || string.IsNullOrWhiteSpace(applicationUser.Email))
        {
            return null;
        }

        return new BillingUser(applicationUser.Id, applicationUser.Email);
    }

    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (SubscriptionPlanNotFoundException exception)
        {
            return Results.Problem(
                title: "Subscription plan not found",
                detail: exception.Message,
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (SubscriptionEnrollmentInProgressException exception)
        {
            return Results.Problem(
                title: "Subscription enrollment in progress",
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (MaxioApiException exception)
        {
            return Results.Problem(
                title: "Subscription billing provider error",
                detail: exception.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (HttpRequestException)
        {
            return Results.Problem(
                title: "Subscription billing provider unavailable",
                detail: "The subscription billing provider could not be reached.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException)
        {
            return Results.Problem(
                title: "Subscription billing provider timeout",
                detail: "The subscription billing provider did not respond in time.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }
}
