using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointResults
{
    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (SubscriptionIdentityException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authenticated user not found",
                detail: exception.Message);
        }
        catch (SubscriptionPlanNotFoundException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Subscription plan not found",
                detail: exception.Message);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Maxio rejected the subscription request",
                detail: string.Join(" ", exception.Errors));
        }
        catch (MaxioApiException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Subscription billing is temporarily unavailable");
        }
        catch (HttpRequestException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Subscription billing is temporarily unavailable");
        }
        catch (TaskCanceledException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "Subscription billing request timed out");
        }
    }
}
