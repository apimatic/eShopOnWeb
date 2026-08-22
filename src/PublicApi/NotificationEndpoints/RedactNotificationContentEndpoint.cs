using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new RedactNotificationContentRequest(notificationId), notifications);
            })
            .Produces<OrderNotificationDto>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactNotificationContentRequest request, IOrderNotificationService notifications)
    {
        await notifications.RedactContentAsync(request.NotificationId);
        var updated = await notifications.GetByIdAsync(request.NotificationId);
        if (updated == null)
        {
            throw new EntityNotFoundException("Notification not found.");
        }

        return Results.Ok(OrderNotificationDtoMapper.ToDto(updated));
    }
}
