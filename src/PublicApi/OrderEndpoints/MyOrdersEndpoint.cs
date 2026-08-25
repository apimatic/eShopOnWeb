using System.Collections.Generic;
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

public record OrderSummaryDto(
    int Id,
    decimal Total,
    string PaymentStatus,
    string? AuthorizationId,
    string? CaptureId,
    string? CapturedAmount,
    List<RefundDto> Refunds
);

public record RefundDto(string RefundId, decimal Amount, string IdempotencyKey);

public class MyOrdersEndpoint : IEndpoint<IResult, IRepository<Order>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<Order> orderRepo) =>
            {
                return await HandleAsync(orderRepo);
            })
            .Produces<List<OrderSummaryDto>>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IRepository<Order> orderRepo)
    {
        var httpCtx = _httpContextAccessor.HttpContext;
        var ct = httpCtx?.RequestAborted ?? default;
        var user = httpCtx?.User;
        var buyerId = user?.FindFirstValue(ClaimTypes.Email)
                   ?? user?.FindFirstValue("sub")
                   ?? user?.Identity?.Name;

        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var spec = new CustomerOrdersWithPaymentSpec(buyerId);
        var orders = await orderRepo.ListAsync(spec, ct);

        var dtos = orders.Select(o => new OrderSummaryDto(
            o.Id,
            o.Total(),
            o.PaymentStatus.ToString(),
            o.AuthorizationId,
            o.CaptureId,
            o.CapturedAmount,
            o.Refunds.Select(r => new RefundDto(r.PayPalRefundId, r.Amount, r.IdempotencyKey)).ToList()
        )).ToList();

        return Results.Ok(dtos);
    }
}
