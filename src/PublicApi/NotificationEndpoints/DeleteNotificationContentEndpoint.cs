using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DeleteNotificationContentResponse
{
    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(notificationId, notificationService);
            })
            .Produces<DeleteNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService notificationService)
    {
        var notification = await notificationService.RedactContentAsync(notificationId);
        if (notification == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new DeleteNotificationContentResponse
        {
            NotificationId = notification.Id,
            ContentRedacted = notification.ContentRedacted,
            Status = notification.ProviderStatus,
            ProviderMessageSid = notification.ProviderMessageSid
        });
    }
}
