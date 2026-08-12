using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for one of the shopper's own orders, and what became of each message. Each entry carries
/// its own notificationId — the identifier the operator endpoints act on. Delivery outcomes are refreshed
/// from the provider.
/// </summary>
public class OrderNotificationsEndpoint : AuthenticatedEndpointBase,
    IEndpoint<IResult, OrderIdRequest, IOrderNotificationService>
{
    public OrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
                await HandleAsync(new OrderIdRequest(orderId), service))
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request, IOrderNotificationService service)
    {
        var notifications = await service.GetOrderNotificationsAsync(request.OrderId, BuyerId, RequestAborted);

        var response = new OrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(n => n.ToDto()).ToList()
        };
        return Results.Ok(response);
    }
}
