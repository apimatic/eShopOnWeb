using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Lists the caller's orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IOrderNotificationService service) =>
                await HandleAsync(http, service, http.RequestAborted))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, IOrderNotificationService service, CancellationToken ct)
    {
        var buyerId = http.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var orders = await service.GetMyOrdersAsync(buyerId, ct);

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(ow => new MyOrderDto
            {
                OrderId = ow.Order.Id,
                Status = ow.Order.Status.ToString(),
                OrderDate = ow.Order.OrderDate,
                Total = ow.Order.Total(),
                Items = ow.Order.OrderItems.Select(i => new OrderLineViewDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Notifications = ow.Notifications.Select(n => n.ToView()).ToList()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
