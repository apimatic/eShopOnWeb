using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Disposes of a message's content (operator action), both locally and at the
/// provider, so the text is no longer retrievable from either. The record that
/// a message was sent, and its outcome, survives.
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
        var response = new DeleteNotificationContentResponse(request.CorrelationId());

        try
        {
            var notification = await notificationService.RedactContentAsync(request.NotificationId);
            response.NotificationId = notification.Id;
            response.ContentRedacted = notification.ContentRedacted;
            response.Status = notification.ProviderStatus;
            return Results.Ok(response);
        }
        catch (NotificationNotFoundException)
        {
            return Results.NotFound();
        }
    }
}
