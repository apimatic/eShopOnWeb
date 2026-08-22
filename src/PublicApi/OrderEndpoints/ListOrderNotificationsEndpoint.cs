using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public bool IsAdministrator { get; set; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IRepository<Order>>
{
    private readonly IOrderSmsNotificationService _notifications;

    public ListOrderNotificationsEndpoint(IOrderSmsNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext httpContext, IRepository<Order> orderRepository) =>
            {
                var request = new ListOrderNotificationsRequest
                {
                    OrderId = orderId,
                    BuyerId = httpContext.GetRequiredBuyerId(),
                    IsAdministrator = httpContext.IsAdministrator()
                };
                return await HandleAsync(request, orderRepository);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IRepository<Order> orderRepository)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        if (!request.IsAdministrator && order.BuyerId != request.BuyerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notifications.ListForOrderAsync(order.Id);
        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = order.Id,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        });
    }
}
