using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; set; }
}

public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationContentRequest, IOrderSmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, IOrderSmsNotificationService notifications) =>
            {
                return await HandleAsync(new RedactNotificationContentRequest { NotificationId = notificationId }, notifications);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactNotificationContentRequest request, IOrderSmsNotificationService notifications)
    {
        await notifications.RedactContentAsync(request.NotificationId);
        return Results.NoContent();
    }
}
