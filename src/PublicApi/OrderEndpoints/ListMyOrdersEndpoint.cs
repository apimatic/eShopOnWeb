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

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, EmptyRequest, IShopOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IShopOrderService orderService) =>
            {
                return await HandleAsync(new EmptyRequest(), orderService, httpContext);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, IShopOrderService orderService)
        => HandleAsync(request, orderService, null!);

    private async Task<IResult> HandleAsync(EmptyRequest request, IShopOrderService orderService, HttpContext httpContext)
    {
        var buyerId = httpContext.GetBuyerId();
        var orders = await orderService.ListBuyerOrdersAsync(buyerId);
        var notifications = await orderService.ListNotificationsForBuyerOrdersAsync(buyerId);
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.Select(NotificationDto.From).ToList());

        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                Total = o.Total(),
                Notifications = byOrder.TryGetValue(o.Id, out var n) ? n : new List<NotificationDto>()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
