using System;
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

public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, IShopperOrderService>
{
    private readonly IOrderNotificationService _notifications;

    public GetMyOrdersEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IShopperOrderService service, HttpContext http) =>
            {
                return await HandleAsync(new GetMyOrdersRequest(), service, http);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(GetMyOrdersRequest request, IShopperOrderService service)
        => throw new NotSupportedException();

    private async Task<IResult> HandleAsync(GetMyOrdersRequest request, IShopperOrderService service, HttpContext http)
    {
        var buyerId = CallerIdentity.RequireBuyerId(http);
        var orders = await service.ListForBuyerAsync(buyerId, http.RequestAborted);
        var response = new GetMyOrdersResponse(request.CorrelationId());

        foreach (var order in orders)
        {
            var notifications = await _notifications.ListForOrderAsync(order.Id, buyerId, allowAnyBuyer: false, http.RequestAborted);
            response.Orders.Add(new ShopperOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Notifications = notifications.Select(NotificationMapper.ToDto).ToList()
            });
        }

        return Results.Ok(response);
    }
}
