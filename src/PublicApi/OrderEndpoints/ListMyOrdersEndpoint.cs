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
/// The signed-in shopper's orders with their payment state.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderQueryService orderQueryService, CancellationToken ct) =>
            {
                return await HandleAsync(user, orderQueryService, ct);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderQueryService orderQueryService, CancellationToken ct)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await orderQueryService.ListOrdersWithPaymentsAsync(buyerId, ct);

        var response = new ListMyOrdersResponse();
        response.Orders.AddRange(orders.Select(o => new MyOrderDto
        {
            OrderId = o.Order.Id,
            OrderDate = o.Order.OrderDate,
            Status = o.Order.Status.ToString(),
            Total = o.Order.Total(),
            Items = o.Order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = o.Payment == null
                ? null
                : new MyOrderPaymentDto
                {
                    PaymentId = o.Payment.Id,
                    Status = o.Payment.Status.ToString(),
                    Amount = o.Payment.Amount,
                    Currency = o.Payment.Currency,
                    AuthorizationId = o.Payment.AuthorizationId,
                    AuthorizationStatus = o.Payment.AuthorizationStatus,
                    AuthorizationExpiresAt = o.Payment.AuthorizationExpiresAt,
                    CaptureId = o.Payment.CaptureId,
                    CapturedAmount = o.Payment.CapturedAmount,
                    PayPalFee = o.Payment.PayPalFee,
                    NetAmount = o.Payment.NetAmount,
                    Refunds = o.Payment.Refunds.Select(r => new MyOrderRefundDto
                    {
                        RefundId = r.Id,
                        PayPalRefundId = r.PayPalRefundId,
                        Amount = r.Amount,
                        Status = r.Status,
                        CreatedAt = r.CreatedAt
                    }).ToList()
                }
        }));

        return Results.Ok(response);
    }
}
