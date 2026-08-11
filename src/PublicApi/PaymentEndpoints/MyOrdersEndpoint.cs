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

/// <summary>The caller's own orders, each with its payment state.</summary>
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
                var buyerId = user.BuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var orders = await orderRepository.ListAsync(
                    new CustomerOrdersWithItemsSpecification(buyerId), ct);
                var payments = await paymentRepository.ListAsync(
                    new PaymentsByBuyerSpecification(buyerId), ct);
                var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

                var response = new MyOrdersResponse
                {
                    Orders = orders.Select(order =>
                    {
                        paymentsByOrder.TryGetValue(order.Id, out var payment);
                        return new MyOrderDto
                        {
                            OrderId = order.Id,
                            OrderDate = order.OrderDate,
                            Total = order.Total(),
                            PaymentStatus = PaymentMapping.OrderStatus(payment),
                            Items = order.ToLineDtos(),
                            Payment = PaymentMapping.ToPaymentDto(payment)
                        };
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("PaymentEndpoints");
    }
}
