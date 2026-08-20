using System.Linq;
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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;

    public ListOrderNotificationsEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext httpContext, IRepository<Order> orderRepository) =>
            {
                var buyerId = httpContext.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var order = await orderRepository.FirstOrDefaultAsync(new OrderByIdSpecification(orderId));
                if (order is null)
                {
                    return Results.NotFound();
                }

                if (!httpContext.IsAdministrator() && order.BuyerId != buyerId)
                {
                    return Results.NotFound();
                }

                var notifications = await _notifications.ListForOrderAsync(orderId);
                return Results.Ok(new ListOrderNotificationsResponse
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    Notifications = notifications.Select(NotificationDto.From).ToList()
                });
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public System.Threading.Tasks.Task<IResult> HandleAsync(int request, IRepository<Order> orderRepository)
        => throw new System.NotSupportedException();
}

public class ListOrderNotificationsResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public System.Collections.Generic.List<NotificationDto> Notifications { get; set; } = new();
}
