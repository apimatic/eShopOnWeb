using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    private readonly IOrderNotificationService _notificationService;

    public GetOrderNotificationsEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext httpContext, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(orderId, orderRepository, httpContext);
            })
            .Produces<GetOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepository)
    {
        return HandleAsync(orderId, orderRepository, null!);
    }

    private async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepository, HttpContext httpContext)
    {
        var buyerId = httpContext.User.RequireBuyerId();
        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null)
        {
            return Results.NotFound();
        }

        if (!httpContext.User.IsAdministrator() && order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationService.ListForOrderAsync(orderId);
        return Results.Ok(new GetOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
        });
    }
}
