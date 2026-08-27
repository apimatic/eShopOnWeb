using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the caller's orders with their payment state.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService orderPaymentService, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null)
                {
                    return Results.Unauthorized();
                }

                var orders = await orderPaymentService.ListMyOrdersAsync(buyerId, ct);
                var payments = await orderPaymentService.GetPaymentsForOrdersAsync(
                    orders.Select(o => o.Id).ToList(), ct);

                var response = new ListMyOrdersResponse
                {
                    Orders = orders.Select(o => new MyOrderDto
                    {
                        OrderId = o.Id,
                        OrderDate = o.OrderDate,
                        Status = o.Status.ToString(),
                        Total = o.Total(),
                        Items = o.OrderItems.Select(i => new CreateOrderItemDto
                        {
                            CatalogItemId = i.ItemOrdered.CatalogItemId,
                            Name = i.ItemOrdered.ProductName,
                            UnitPrice = i.UnitPrice,
                            Units = i.Units
                        }).ToList(),
                        Payment = payments.FirstOrDefault(p => p.OrderId == o.Id) is { } payment
                            ? PaymentDto.FromPayment(payment)
                            : null
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}
