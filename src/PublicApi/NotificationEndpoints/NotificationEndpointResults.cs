using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

internal static class NotificationEndpointResults
{
    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (InvalidDestinationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (NotificationValidationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (NotificationResourceNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (NotificationConflictException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (MessageProviderException ex) when ((int?)ex.StatusCode == 429)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Messaging provider temporarily unavailable.");
        }
        catch (MessageProviderException)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Messaging provider unavailable.");
        }
    }
}
