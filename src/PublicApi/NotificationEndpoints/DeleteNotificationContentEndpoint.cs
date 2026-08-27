using System;
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
/// Disposes of the content of a message (operator). The text is redacted at
/// the provider as well as locally; the record that a message was sent, and
/// what became of it, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, DeleteNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(new DeleteNotificationContentRequest(notificationId), notificationService);
            })
            .Produces<DeleteNotificationContentResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteNotificationContentRequest request, IOrderNotificationService notificationService)
    {
        try
        {
            var notification = await notificationService.DisposeContentAsync(request.NotificationId);
            if (notification is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new DeleteNotificationContentResponse(request.CorrelationId())
            {
                NotificationId = notification.Id,
                ContentDisposed = notification.ContentDisposed,
                Status = notification.Status
            });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}
