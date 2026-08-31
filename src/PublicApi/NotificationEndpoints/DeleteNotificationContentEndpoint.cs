using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DeleteNotificationContentResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}

/// <summary>
/// Disposes of a message's content (operator): the text is redacted at the
/// provider and removed locally, while the fact the message was sent and its
/// outcome survive.
/// </summary>
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
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService notificationService)
    {
        var redacted = await notificationService.RedactContentAsync(notificationId);
        if (!redacted)
        {
            return Results.NotFound();
        }

        return Results.Ok(new DeleteNotificationContentResponse
        {
            NotificationId = notificationId,
            ContentRedacted = true
        });
    }
}
