using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Shows what was sent for one of the signed-in shopper's orders, and what became of each message.
/// </summary>
public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IOrderApiService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderApiService orderService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(
                    new GetOrderNotificationsRequest(orderId) { BuyerId = user.GetBuyerId(), CancellationToken = cancellationToken },
                    orderService);
            })
            .Produces<GetOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IOrderApiService orderService)
    {
        if (request.BuyerId is null)
        {
            return Results.Unauthorized();
        }

        var notifications = await orderService.GetOrderNotificationsAsync(request.BuyerId, request.OrderId, request.CancellationToken);
        if (notifications is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new GetOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(OrderNotificationDto.FromEntity).ToList()
        });
    }
}
