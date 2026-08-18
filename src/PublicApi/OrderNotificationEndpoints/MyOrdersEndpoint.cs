using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// The caller's own orders, each showing where its notifications got to (delivery outcomes refreshed).
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly IOrderNotificationService _service;

    public MyOrdersEndpoint(IOrderNotificationService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http) =>
            {
                return await HandleAsync(http);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var buyerId = CallerIdentity.Of(http.User);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await _service.GetOrdersForBuyerAsync(buyerId, http.RequestAborted);
        var notifications = await _service.GetNotificationsForBuyerAsync(buyerId, http.RequestAborted);
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new MyOrdersResponse
        {
            Orders = orders
                .Select(o => OrderSummaryDto.From(
                    o,
                    byOrder.TryGetValue(o.Id, out var ns)
                        ? ns
                        : new List<ApplicationCore.Entities.OrderNotificationAggregate.OrderNotification>()))
                .ToList()
        };
        return Results.Ok(response);
    }
}
