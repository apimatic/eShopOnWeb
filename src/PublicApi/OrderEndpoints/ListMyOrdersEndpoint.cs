using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, string, IReadRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IReadRepository<Order> orderRepo, HttpContext httpContext) =>
            {
                var buyerId = httpContext.User.Identity!.Name!;
                return await HandleAsync(buyerId, orderRepo);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IReadRepository<Order> orderRepo)
    {
        var spec = new CustomerOrdersSpecification(buyerId);
        var orders = await orderRepo.ListAsync(spec);

        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                Id = o.Id,
                OrderDate = o.OrderDate,
                Total = o.Total(),
                PaymentStatus = o.PaymentStatus.ToString(),
                PayPalOrderId = o.PayPalOrderId,
                CaptureId = o.CaptureId,
                TotalRefunded = o.TotalRefunded,
                Items = o.OrderItems.Select(i => new MyOrderItemDto
                {
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Units
                }).ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
