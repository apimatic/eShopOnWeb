using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointResults
{
    public static async Task<IResult> ExecuteAsync(HttpContext context, Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ShopperNotFoundException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: exception.Message);
        }
        catch (SubscriptionPlanNotFoundException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Subscription plan not found", detail: exception.Message);
        }
        catch (SubscriptionEnrollmentInProgressException exception)
        {
            context.Response.Headers.RetryAfter = "5";
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Subscription enrollment in progress", detail: exception.Message);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Maxio rejected the subscription request",
                detail: "The billing provider could not enroll the shopper in this plan.");
        }
        catch (MaxioConfigurationException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Subscription billing is not configured", detail: exception.Message);
        }
        catch (MaxioTransportException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Subscription billing is temporarily unavailable",
                detail: "The enrollment is protected from duplicate creation; retry shortly.");
        }
        catch (MaxioApiException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Subscription billing provider error",
                detail: "Maxio Advanced Billing returned an unsuccessful response.");
        }
        catch (Exception exception) when (exception is MaxioContractException or SubscriptionOwnershipException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Subscription billing data error",
                detail: "Maxio Advanced Billing returned data that could not be safely associated with this shopper.");
        }
    }
}
