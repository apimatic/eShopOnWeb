using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of a message's content at the shopper's request — locally and at the
/// provider (so the text is no longer retrievable there either) — while the fact that a message was
/// sent, and what became of it, survives. Restricted to the administrator role.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(notificationId, notificationService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService notificationService)
    {
        var existing = await notificationService.FindNotificationAsync(notificationId);
        if (existing is null)
        {
            return Results.NotFound();
        }

        try
        {
            await notificationService.DisposeContentAsync(notificationId);
        }
        catch (SmsGatewayException ex)
        {
            // The caller asked for the content to be genuinely gone; if the provider redaction failed,
            // say so rather than reporting success.
            return Results.Json(new { message = $"The message content could not be disposed of at the provider: {ex.Message}" },
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.NoContent();
    }
}
