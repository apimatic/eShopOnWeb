using System.Linq;
using System.Security.Claims;
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

public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IReadRepository<Order> orderRepo, ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var orders = await orderRepo.ListAsync(new CustomerOrdersWithPaymentSpec(buyerId));

                var result = orders.Select(o => new
                {
                    orderId = o.Id,
                    orderDate = o.OrderDate,
                    status = o.Status.ToString(),
                    total = o.Total(),
                    capturedAmount = o.CapturedAmount,
                    payPalFee = o.PayPalFee,
                    netAmount = o.NetAmount,
                    payPalOrderId = o.PayPalOrderId,
                    authorizationId = o.AuthorizationId,
                    captureId = o.CaptureId,
                    items = o.OrderItems.Select(i => new
                    {
                        catalogItemId = i.ItemOrdered.CatalogItemId,
                        productName = i.ItemOrdered.ProductName,
                        unitPrice = i.UnitPrice,
                        quantity = i.Units
                    }),
                    refunds = o.Refunds.Select(r => new
                    {
                        refundId = r.RefundId,
                        amount = r.Amount,
                        idempotencyKey = r.IdempotencyKey,
                        createdAt = r.CreatedAt
                    })
                });

                return Results.Ok(result);
            })
            .WithTags("OrderEndpoints");
    }
}
