using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, BuyerScopedRequest, IOrderMessagingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IOrderMessagingService orderMessagingService) =>
            {
                return await HandleAsync(new BuyerScopedRequest { BuyerId = ApiUser.BuyerId(httpContext) }, orderMessagingService);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(BuyerScopedRequest request, IOrderMessagingService orderMessagingService)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await orderMessagingService.ListMyOrdersAsync(request.BuyerId, default);
        var response = new ListMyOrdersResponse(request.CorrelationId());
        response.Orders.AddRange(orders.Select(o => new ShopperOrderDto
        {
            OrderId = o.OrderId,
            Status = o.Status,
            Total = o.Total,
            OrderDate = o.OrderDate,
            Notifications = o.Notifications.Select(OrderNotificationDto.From).ToList()
        }));
        return Results.Ok(response);
    }
}
