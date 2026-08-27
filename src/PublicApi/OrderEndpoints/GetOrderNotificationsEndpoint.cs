using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Shows what was sent for an order and what became of each message.
/// Shoppers can only see their own orders; operators can see any.
/// </summary>
public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService notificationService) =>
            {
                var request = new GetOrderNotificationsRequest(orderId)
                {
                    CallerId = user.Identity!.Name!,
                    IsOperator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
                };
                return await HandleAsync(request, notificationService);
            })
            .Produces<GetOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IOrderNotificationService notificationService)
    {
        var notifications = await notificationService.GetOrderNotificationsAsync(request.OrderId, request.CallerId, request.IsOperator);
        if (notifications is null)
        {
            return Results.NotFound();
        }

        var response = new GetOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId
        };
        response.Notifications.AddRange(notifications.Select(NotificationDto.FromEntity));
        return Results.Ok(response);
    }
}
