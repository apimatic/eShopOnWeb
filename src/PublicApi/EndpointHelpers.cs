using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class EndpointHelpers
{
    public static string RequireBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }

        return buyerId;
    }

    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (InvalidContactNumberException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (ResourceNotFoundException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
        catch (OrderNotificationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
    }
}
