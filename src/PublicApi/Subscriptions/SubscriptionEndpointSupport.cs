using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal static class SubscriptionEndpointSupport
{
    public static async Task<ApplicationUser?> FindUserAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(userName)
            ? null
            : await userManager.FindByNameAsync(userName);
    }

    public static async Task<IResult> ExecuteAsync(
        Func<Task<IResult>> operation,
        ILogger logger)
    {
        try
        {
            return await operation();
        }
        catch (SubscriptionBillingException ex)
        {
            logger.LogWarning(
                "Subscription billing failed with {BillingError}; provider status {ProviderStatus}.",
                ex.Error,
                ex.ProviderStatusCode);

            var status = ex.Error switch
            {
                SubscriptionBillingError.InvalidRequest => StatusCodes.Status400BadRequest,
                SubscriptionBillingError.NotFound => StatusCodes.Status404NotFound,
                SubscriptionBillingError.Conflict => StatusCodes.Status409Conflict,
                SubscriptionBillingError.ProviderRejected
                    when ex.ProviderStatusCode is >= 400 and < 500 => ex.ProviderStatusCode.Value,
                SubscriptionBillingError.ProviderRejected => StatusCodes.Status422UnprocessableEntity,
                SubscriptionBillingError.ProviderUnavailable => StatusCodes.Status503ServiceUnavailable,
                SubscriptionBillingError.ProviderContract => StatusCodes.Status502BadGateway,
                SubscriptionBillingError.Indeterminate => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status502BadGateway
            };

            return Results.Problem(
                statusCode: status,
                title: Title(ex.Error),
                detail: ex.Message);
        }
    }

    private static string Title(SubscriptionBillingError error) => error switch
    {
        SubscriptionBillingError.InvalidRequest => "Invalid subscription request",
        SubscriptionBillingError.NotFound => "Subscription resource not found",
        SubscriptionBillingError.Conflict => "Subscription enrollment conflict",
        SubscriptionBillingError.ProviderRejected => "Subscription request rejected",
        SubscriptionBillingError.ProviderUnavailable => "Billing provider unavailable",
        SubscriptionBillingError.ProviderContract => "Billing provider response invalid",
        SubscriptionBillingError.Indeterminate => "Subscription outcome indeterminate",
        _ => "Subscription billing failed"
    };
}
