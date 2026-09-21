using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for one of the shopper's orders, and what became of each message. Scoped to the
/// caller: another shopper's order is not visible.
/// </summary>
public class OrderNotificationsEndpoint
    : IEndpoint<IResult, OrderNotificationsRequest, IOrderNotificationService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null)
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(new OrderNotificationsRequest(orderId, buyerId), service, ct);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        OrderNotificationsRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        var notifications = await service.GetOrderNotificationsForBuyerAsync(request.OrderId, request.BuyerId, ct);
        if (notifications is null)
        {
            return Results.NotFound();
        }

        var response = new OrderNotificationsResponse(
            request.OrderId,
            notifications.Select(NotificationMapping.ToSummary).ToList());
        return Results.Ok(response);
    }
}
