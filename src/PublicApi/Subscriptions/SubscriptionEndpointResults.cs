using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal static class SubscriptionEndpointResults
{
    public static async Task<IResult> ExecuteAsync<T>(
        Func<Task<T>> action,
        ILogger logger,
        Func<T, IResult> success)
    {
        try
        {
            return success(await action());
        }
        catch (SubscriptionPlanNotFoundException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Subscription plan not found",
                detail: exception.Message);
        }
        catch (SubscriptionEnrollmentInProgressException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Subscription enrollment in progress",
                detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid subscription request",
                detail: exception.Message);
        }
        catch (Exception exception) when (exception is MaxioApiException or HttpRequestException or TaskCanceledException)
        {
            logger.LogError(exception, "The Maxio billing request failed.");
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Billing service unavailable",
                detail: "The billing provider could not complete the request. Please retry later.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The subscription request failed unexpectedly.");
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Subscription request failed",
                detail: "The subscription request could not be completed.");
        }
    }
}
