using System;
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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// List the caller's orders with their payment state.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<Order> orderRepository, IRepository<Payment> paymentRepository,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var orders = await orderRepository.ListAsync(
                    new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
                var payments = await paymentRepository.ListAsync(
                    new PaymentsByOrderIdsSpec(orders.Select(o => o.Id)), cancellationToken);

                var response = new ListMyOrdersResponse();
                foreach (var order in orders.OrderByDescending(o => o.OrderDate))
                {
                    var payment = payments.FirstOrDefault(p => p.OrderId == order.Id);
                    response.Orders.Add(new OrderDto
                    {
                        OrderId = order.Id,
                        OrderDate = order.OrderDate,
                        Total = order.Total(),
                        PaymentStatus = payment?.Status.ToString() ?? "AwaitingPayment",
                        Items = order.OrderItems.Select(i => new OrderItemDto
                        {
                            CatalogItemId = i.ItemOrdered.CatalogItemId,
                            ProductName = i.ItemOrdered.ProductName,
                            UnitPrice = i.UnitPrice,
                            Units = i.Units
                        }).ToList(),
                        Payment = payment is null ? null : PaymentDto.FromPayment(payment)
                    });
                }

                return Results.Ok(response);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public System.Collections.Generic.List<OrderDto> Orders { get; set; } = new();
}
