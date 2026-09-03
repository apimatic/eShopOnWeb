using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IShopperOrderService orderService, HttpContext httpContext) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(), httpContext, orderService);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListMyOrdersRequest request, IShopperOrderService orderService)
        => HandleAsync(request, null!, orderService);

    private async Task<IResult> HandleAsync(
        ListMyOrdersRequest request,
        HttpContext httpContext,
        IShopperOrderService orderService)
    {
        var response = new ListMyOrdersResponse(request.CorrelationId());
        var orders = await orderService.ListMyOrdersAsync(httpContext.GetBuyerId(), httpContext.RequestAborted);
        response.Orders.AddRange(orders.Select(summary => new MyOrderDto
        {
            OrderId = summary.Order.Id,
            Status = summary.Order.Status.ToString(),
            Total = summary.Order.Total(),
            OrderDate = summary.Order.OrderDate,
            Notifications = summary.Notifications.Select(OrderNotificationDto.From).ToList()
        }));
        return Results.Ok(response);
    }
}
