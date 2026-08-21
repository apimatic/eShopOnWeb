using System;
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
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
    public OrderPaymentDto? Payment { get; set; }
}

/// <summary>Returns the signed-in shopper's own orders together with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var orders = await service.GetOrdersForBuyerAsync(buyerId);
                var response = orders.Select(o => new MyOrderDto
                {
                    OrderId = o.Order.Id,
                    OrderDate = o.Order.OrderDate,
                    Total = o.Order.Total(),
                    Items = o.Order.OrderItems.Select(i => new MyOrderItemDto
                    {
                        CatalogItemId = i.ItemOrdered.CatalogItemId,
                        ProductName = i.ItemOrdered.ProductName,
                        UnitPrice = i.UnitPrice,
                        Units = i.Units
                    }).ToList(),
                    Payment = o.Payment is null ? null : OrderPaymentDto.From(o.Payment)
                }).ToList();

                return Results.Ok(response);
            })
            .Produces<List<MyOrderDto>>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderPaymentService service) =>
        Task.FromResult<IResult>(Results.Ok());
}
