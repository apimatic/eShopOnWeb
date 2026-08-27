using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new RedactNotificationContentRequest(notificationId), notifications);
            })
            .Produces<OrderNotificationDto>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactNotificationContentRequest request, IOrderNotificationService notifications)
    {
        var notification = await notifications.RedactContentAsync(request.NotificationId);
        return Results.Ok(OrderNotificationDto.From(notification));
    }
}

public class RedactNotificationContentRequest : BaseRequest
{
    public RedactNotificationContentRequest(int notificationId) => NotificationId = notificationId;
    public int NotificationId { get; }
}
