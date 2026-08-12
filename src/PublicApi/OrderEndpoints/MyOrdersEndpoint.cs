using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The signed-in shopper's orders, each showing where its notifications got to (delivery outcomes
/// refreshed from the provider). Only the caller's own orders.
/// </summary>
public class MyOrdersEndpoint : AuthenticatedEndpointBase,
    IEndpoint<IResult, MyOrdersRequest, IOrderNotificationService>
{
    public MyOrdersEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService service) =>
                await HandleAsync(new MyOrdersRequest(), service))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderNotificationService service)
    {
        var orders = await service.GetMyOrdersAsync(BuyerId, RequestAborted);

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => new OrderSummaryDto
            {
                OrderId = o.Order.Id,
                OrderDate = o.Order.OrderDate,
                Status = o.Order.Status.ToString(),
                Total = o.Order.Total(),
                Items = o.Order.OrderItems.Select(oi => new OrderItemDto
                {
                    CatalogItemId = oi.ItemOrdered.CatalogItemId,
                    ProductName = oi.ItemOrdered.ProductName,
                    UnitPrice = oi.UnitPrice,
                    Units = oi.Units
                }).ToList(),
                Notifications = o.Notifications.Select(n => n.ToDto()).ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
