using System.Threading;
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
/// Operator action for a shopper's request to dispose of a message's content. Afterwards the text is
/// no longer retrievable from the provider either, while the fact that a message was sent and what
/// became of it survives.
/// </summary>
public class RedactNotificationContentEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(notificationId, notifications);
            })
            .Produces<RedactContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService notifications)
    {
        var notification = await notifications.RedactContentAsync(notificationId, CancellationToken.None);
        if (notification is null)
        {
            return Results.NotFound();
        }

        var response = new RedactContentResponse
        {
            NotificationId = notification.Id,
            ContentRedacted = notification.ContentRedacted,
            Status = notification.Status.ToString(),
            ProviderMessageSid = notification.ProviderMessageSid
        };
        return Results.Ok(response);
    }
}
