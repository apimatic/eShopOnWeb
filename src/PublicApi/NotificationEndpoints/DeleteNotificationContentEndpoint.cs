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
/// Operator action: disposes of a message's content. The text is redacted at the provider
/// (not merely hidden here); the record that a message was sent, and its outcome, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, DeleteNotificationContentRequest, INotificationOperationsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationOperationsService operationsService) =>
            {
                return await HandleAsync(new DeleteNotificationContentRequest(notificationId), operationsService);
            })
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteNotificationContentRequest request, INotificationOperationsService operationsService)
    {
        var result = await operationsService.DisposeContentAsync(request.NotificationId);

        if (result.NotificationNotFound)
        {
            return Results.NotFound(new { error = result.Error });
        }

        if (!result.Succeeded)
        {
            return Results.Conflict(new { error = result.Error });
        }

        return Results.NoContent();
    }
}

public class DeleteNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; }

    public DeleteNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }
}
