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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

/// <summary>Lists the signed-in shopper's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IReadRepository<Order> orderRepository, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);

                var response = new MyOrdersResponse
                {
                    Orders = orders.Select(order => new OrderSummaryDto
                    {
                        OrderId = order.Id,
                        OrderDate = order.OrderDate,
                        Total = order.Total(),
                        PaymentStatus = order.PaymentStatus.ToString(),
                        PayPalOrderId = order.PayPalOrderId,
                        PayPalRefundId = order.PayPalRefundId,
                        Items = order.OrderItems.Select(oi => new OrderItemDto
                        {
                            CatalogItemId = oi.ItemOrdered.CatalogItemId,
                            ProductName = oi.ItemOrdered.ProductName,
                            UnitPrice = oi.UnitPrice,
                            Units = oi.Units
                        }).ToList()
                    }).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>(StatusCodes.Status200OK)
            .WithTags("OrderEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Lists the caller's orders", "Returns the signed-in shopper's orders and their payment state."));
    }
}
