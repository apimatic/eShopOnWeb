using System;
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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(
                    new ListOrderNotificationsRequest
                    {
                        OrderId = orderId,
                        BuyerId = httpContext.GetBuyerId(),
                        IsAdministrator = httpContext.IsAdministrator()
                    },
                    orderRepository);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IRepository<Order> orderRepository)
    {
        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null || (!request.IsAdministrator && order.BuyerId != request.BuyerId))
        {
            return Results.NotFound();
        }

        var notifications = await _notificationService.ListForOrderAsync(order.Id);
        var response = new ListOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };

        return Results.Ok(response);
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public bool IsAdministrator { get; set; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
