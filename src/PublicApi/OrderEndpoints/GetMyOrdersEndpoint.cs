using System.Collections.Generic;
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

public class GetMyOrdersEndpoint : IEndpoint<IResult, EmptyMyOrdersRequest, IShopperOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderNotificationQueryService _notificationQueries;

    public GetMyOrdersEndpoint(
        IHttpContextAccessor httpContextAccessor,
        IOrderNotificationQueryService notificationQueries)
    {
        _httpContextAccessor = httpContextAccessor;
        _notificationQueries = notificationQueries;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IShopperOrderService service) =>
            {
                return await HandleAsync(new EmptyMyOrdersRequest(), service);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyMyOrdersRequest request, IShopperOrderService service)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.RequireBuyerId()!;
        var orders = await service.ListBuyerOrdersAsync(buyerId);

        var summaries = new List<OrderSummaryDto>();
        foreach (var order in orders)
        {
            var notifications = await _notificationQueries.GetForOrderAsync(order.Id);
            summaries.Add(new OrderSummaryDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total(),
                OrderDate = order.OrderDate,
                Notifications = notifications.Select(OrderNotificationDto.From).ToList()
            });
        }

        return Results.Ok(new ListMyOrdersResponse { Orders = summaries });
    }
}

public class EmptyMyOrdersRequest
{
}
