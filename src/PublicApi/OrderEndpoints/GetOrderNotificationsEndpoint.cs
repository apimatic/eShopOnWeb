using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, ClaimsPrincipal user, IRepository<Order> orders, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new GetOrderNotificationsRequest { OrderId = orderId }, user, orders, notifications);
            })
            .Produces<GetOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IRepository<Order> orders)
        => HandleAsync(request, new ClaimsPrincipal(), orders, notifications: null!);

    private async Task<IResult> HandleAsync(
        GetOrderNotificationsRequest request,
        ClaimsPrincipal user,
        IRepository<Order> orders,
        IOrderNotificationService notifications)
    {
        var order = await orders.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        if (!user.IsAdministrator() && !string.Equals(order.BuyerId, user.GetBuyerId(), StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var items = await notifications.ListForOrderAsync(
            request.OrderId,
            user.GetBuyerId(),
            user.IsAdministrator());

        var response = new GetOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = items.Select(NotificationDto.From).ToList()
        };

        return Results.Ok(response);
    }
}
