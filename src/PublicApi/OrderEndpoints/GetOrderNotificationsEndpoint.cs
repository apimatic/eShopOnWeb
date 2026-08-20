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

public class GetOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; init; }

    public GetOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class GetOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _notificationService;

    public GetOrderNotificationsEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository, HttpContext httpContext) =>
            {
                return await HandleAsync(new GetOrderNotificationsRequest(orderId), orderRepository, httpContext);
            })
            .Produces<GetOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IRepository<Order> orderRepository)
    {
        return Task.FromResult(Results.NotFound());
    }

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IRepository<Order> orderRepository, HttpContext httpContext)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId);
        if (order == null)
        {
            return Results.NotFound();
        }

        var buyerId = httpContext.User.GetBuyerId();
        var isAdmin = httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        if (!isAdmin && !string.Equals(order.BuyerId, buyerId, System.StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var notifications = await _notificationService.ListForOrderAsync(order.Id, refreshFromProvider: true);
        var response = new GetOrderNotificationsResponse
        {
            OrderId = order.Id,
            Notifications = notifications.Select(OrderNotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
