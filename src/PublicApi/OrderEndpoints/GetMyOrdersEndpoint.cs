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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersEndpoint : IEndpoint<IResult, string, IReadRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IReadRepository<Order> orderRepo, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name ?? "";
                return await HandleAsync(buyerId, orderRepo, ct);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IReadRepository<Order> orderRepo)
        => await HandleAsync(buyerId, orderRepo, default);

    private async Task<IResult> HandleAsync(string buyerId, IReadRepository<Order> orderRepo, CancellationToken ct)
    {
        var spec = new OrdersByBuyerWithPaymentSpec(buyerId);
        var orders = await orderRepo.ListAsync(spec, ct);

        var dtos = orders.Select(o => new OrderDto
        {
            OrderId = o.Id,
            OrderDate = o.OrderDate,
            Status = o.Status.ToString(),
            Total = o.Total(),
            Items = o.OrderItems.Select(i => new OrderItemDto
            {
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = o.Payment is null ? null : new PaymentDto
            {
                AuthorizationId = o.Payment.AuthorizationId,
                AuthorizationStatus = o.Payment.AuthorizationStatus,
                CaptureId = o.Payment.CaptureId,
                CaptureStatus = o.Payment.CaptureStatus,
                CapturedAmount = o.Payment.CapturedAmount,
                PayPalFee = o.Payment.PayPalFee,
                NetAmount = o.Payment.NetAmount,
                Refunds = o.Payment.GetRefunds().Select(r => new RefundDto
                {
                    RefundId = r.RefundId,
                    Amount = r.Amount,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt
                }).ToList()
            }
        }).ToList();

        return Results.Ok(new GetMyOrdersResponse { Orders = dtos });
    }
}
