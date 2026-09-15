using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

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
    public PaymentStateDto? Payment { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }

    public List<MyOrderDto> Orders { get; set; } = new();
}

/// <summary>
/// GET /api/my-orders — the caller's own orders, each with its payment state. Shopper-scoped: a shopper
/// never sees another's orders.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IReadRepository<Order> orderRepository,
                IReadRepository<Payment> paymentRepository,
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);

                var orders = await orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), ct);
                var payments = await paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), ct);
                var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

                var response = new MyOrdersResponse(Guid.NewGuid())
                {
                    Orders = orders
                        .OrderByDescending(o => o.OrderDate)
                        .Select(o => new MyOrderDto
                        {
                            OrderId = o.Id,
                            OrderDate = o.OrderDate,
                            Total = o.Total(),
                            Items = o.OrderItems.Select(oi => new MyOrderItemDto
                            {
                                CatalogItemId = oi.ItemOrdered.CatalogItemId,
                                ProductName = oi.ItemOrdered.ProductName,
                                UnitPrice = oi.UnitPrice,
                                Units = oi.Units
                            }).ToList(),
                            Payment = paymentsByOrder.TryGetValue(o.Id, out var payment)
                                ? PaymentStateDto.From(payment)
                                : null
                        })
                        .ToList()
                };
                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("PaymentEndpoints");
    }
}
