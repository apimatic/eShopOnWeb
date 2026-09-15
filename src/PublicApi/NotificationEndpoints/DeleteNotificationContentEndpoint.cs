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
/// Operator action, on a shopper's request: disposes of the content of a message about them. The text
/// is removed at the provider as well as locally, while the fact that a message was sent and what
/// became of it survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationService notifications) =>
            {
                var result = await notifications.RedactContentAsync(notificationId);
                if (result is null)
                    return Results.NotFound();

                return Results.Ok(new DeleteNotificationContentResponse
                {
                    NotificationId = result.Id,
                    Status = result.Status,
                    ContentRedacted = result.ContentRedacted
                });
            })
            .Produces<DeleteNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }
}

public class DeleteNotificationContentResponse
{
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool ContentRedacted { get; set; }
}
