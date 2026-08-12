using System.Collections.Generic;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

/// <summary>The caller's orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    private readonly IOrderNotificationService _orderNotificationService;

    public MyOrdersEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken ct) => await HandleAsync(user, ct))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.GetUsername();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var orders = await _orderNotificationService.GetOrdersForBuyerAsync(buyerId, ct);

        var response = new MyOrdersResponse();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var notifications = await _orderNotificationService.GetOrderNotificationsAsync(order.Id, ct);
            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Notifications = notifications.Select(NotificationDto.From).ToList()
            });
        }

        return Results.Ok(response);
    }
}
