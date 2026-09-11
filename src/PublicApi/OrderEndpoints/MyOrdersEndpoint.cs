using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string OrderDate { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<PlaceOrderResponseItem> Items { get; set; } = new();
    public PaymentStateDto? Payment { get; set; }
}

/// <summary>The caller's orders with their payment state. Shopper-scoped.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service) =>
                await HandleAsync(user, service))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderPaymentService service)
    {
        var buyerId = CallerIdentity.GetBuyerId(user);
        var results = await service.GetMyOrdersAsync(buyerId);

        var response = new MyOrdersResponse
        {
            Orders = results.Select(r => new MyOrderDto
            {
                OrderId = r.Order.Id,
                OrderDate = r.Order.OrderDate.ToString("o"),
                Total = r.Order.Total(),
                Items = r.Order.OrderItems.Select(oi => new PlaceOrderResponseItem
                {
                    CatalogItemId = oi.ItemOrdered.CatalogItemId,
                    ProductName = oi.ItemOrdered.ProductName,
                    UnitPrice = oi.UnitPrice,
                    Units = oi.Units
                }).ToList(),
                Payment = r.Payment is null ? null : PaymentStateDto.From(r.Payment)
            }).ToList()
        };

        return Results.Ok(response);
    }
}
