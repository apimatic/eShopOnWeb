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

public class GetMyOrdersEndpoint : IEndpoint<IResult, IBuyerOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IBuyerOrderService buyerOrderService) =>
            {
                return await HandleAsync(buyerOrderService);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IBuyerOrderService buyerOrderService)
    {
        var response = new GetMyOrdersResponse();
        var buyerId = _httpContextAccessor.HttpContext!.GetBuyerId();
        var orders = await buyerOrderService.ListMyOrdersAsync(buyerId);

        foreach (var order in orders)
        {
            var notifications = await buyerOrderService.ListNotificationsAsync(buyerId, order.Id);
            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Items = order.OrderItems.Select(OrderItemMapping.ToDto).ToList(),
                Notifications = NotificationMapping.ToDto(notifications).ToList()
            });
        }

        return Results.Ok(response);
    }
}
