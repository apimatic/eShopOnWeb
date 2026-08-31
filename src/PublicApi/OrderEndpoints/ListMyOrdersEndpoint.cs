using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's orders, each showing where its notifications got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IOrderApiService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderApiService orderService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(
                    new ListMyOrdersRequest { BuyerId = user.GetBuyerId(), CancellationToken = cancellationToken },
                    orderService);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderApiService orderService)
    {
        if (request.BuyerId is null)
        {
            return Results.Unauthorized();
        }

        var orders = await orderService.ListMyOrdersAsync(request.BuyerId, request.CancellationToken);

        var response = new ListMyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(o => new OrderDto
            {
                OrderId = o.Order.Id,
                Status = o.Order.Status,
                OrderDate = o.Order.OrderDate,
                Total = o.Order.Total(),
                Items = o.Order.OrderItems.Select(i => new OrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    Units = i.Units,
                    UnitPrice = i.UnitPrice
                }).ToList(),
                Notifications = o.Notifications.Select(OrderNotificationDto.FromEntity).ToList()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
