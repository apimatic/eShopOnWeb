using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared error mapping for subscription endpoints: translates billing-system and
/// domain failures into HTTP responses without leaking Maxio internals to callers.
/// </summary>
public static class SubscriptionEndpointHelpers
{
    public static async Task<IResult> ExecuteAsync(ILogger logger, Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PlanNotFoundException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status404NotFound);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status404NotFound);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (MaxioApiException ex)
        {
            logger.LogError(ex, "Maxio API call failed with status {StatusCode}: {Body}", ex.StatusCode, ex.ResponseBody);
            return Results.Json(new
            {
                error = "The billing system rejected the request.",
                details = ex.Errors
            }, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Maxio integration is misconfigured: {Message}", ex.Message);
            return Results.Json(new { error = "The billing integration is not available." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
