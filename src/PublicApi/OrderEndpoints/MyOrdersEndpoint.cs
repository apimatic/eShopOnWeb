using System;
using System.Linq;
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
/// Lists the caller's own orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest>
{
    private readonly IPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(IPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CancellationToken ct) =>
            {
                return await HandleAsync(new MyOrdersRequest(), ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(MyOrdersRequest request) => HandleAsync(request, CancellationToken.None);

    public async Task<IResult> HandleAsync(MyOrdersRequest request, CancellationToken ct)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await _paymentService.ListMyOrdersAsync(buyerId, ct);

        var response = new MyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(o => new OrderDto
            {
                OrderId = o.Id,
                OrderDate = o.OrderDate,
                Status = o.Status.ToString(),
                Total = o.Total(),
                Currency = o.Currency,
                PayPalOrderId = o.PayPalOrderId,
                AuthorizationId = o.AuthorizationId,
                AuthorizationStatus = o.AuthorizationStatus,
                CaptureId = o.CaptureId,
                CapturedAmount = o.CapturedAmount,
                PayPalFee = o.PayPalFee,
                NetAmount = o.NetAmount,
                TotalRefunded = o.TotalRefunded(),
                Items = o.OrderItems.Select(i => new OrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Refunds = o.Refunds.Select(r => new OrderRefundDto
                {
                    RefundId = r.PayPalRefundId,
                    Amount = r.Amount,
                    Status = r.Status,
                    IdempotencyKey = r.IdempotencyKey,
                    CreatedAt = r.CreatedAt
                }).ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
