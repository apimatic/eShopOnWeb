using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of a message's content on a shopper's request. Afterwards the text is
/// no longer retrievable from the provider either, while the fact it was sent and what became of it
/// survive.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IRepository<OrderNotification> repository, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(notificationId, repository, notificationService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    private static async Task<IResult> HandleAsync(int notificationId, IRepository<OrderNotification> repository, IOrderNotificationService notificationService)
    {
        var notification = await repository.GetByIdAsync(notificationId);
        if (notification is null)
        {
            return Results.NotFound();
        }

        try
        {
            await notificationService.DisposeContentAsync(notification);
        }
        catch (SmsGatewayException ex)
        {
            // The provider would not redact the content; do not report success when the text may survive there.
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.NoContent();
    }
}
