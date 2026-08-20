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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, IOrderSmsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IOrderSmsService service) =>
            {
                var buyerId = httpContext.GetRequiredBuyerId();
                var orders = await service.ListBuyerOrdersAsync(buyerId);
                var notifications = await service.GetNotificationsForOrdersAsync(orders.Select(o => o.Id).ToList());
                var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

                var response = new ListMyOrdersResponse
                {
                    Orders = orders.Select(order =>
                    {
                        byOrder.TryGetValue(order.Id, out var notes);
                        return NotificationDtoMapper.ToOrderDto(order, notes ?? new());
                    }).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderSmsService orderSmsService)
        => Task.FromResult(Results.Ok());
}

public class ListMyOrdersResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}
