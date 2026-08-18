using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services.Sms;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of the content of a message about a shopper. Afterwards the message text is
/// no longer retrievable from the provider either — while the fact it was sent, and what became of it,
/// survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service) => await HandleAsync(notificationId, service))
            .Produces<NotificationDto>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService service)
    {
        try
        {
            var notification = await service.RedactContentAsync(notificationId);
            if (notification is null)
                return Results.NotFound();

            // Reflect the surviving record (status intact, body gone).
            return Results.Ok(NotificationDto.FromEntity(notification));
        }
        catch (TwilioApiException)
        {
            // Provider-side disposal did not succeed; do not claim the content was disposed of.
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}
