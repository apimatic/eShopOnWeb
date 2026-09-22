using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key — repeating with the same key does not refund twice.</summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>POST /api/orders/{orderId}/refunds — refunds the captured payment (full or partial). Shopper-scoped.</summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public RefundOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService service) =>
                await HandleAsync(orderId, request, service))
            .WithTags("PaymentOrderEndpoints");
    }

    private Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, IOrderPaymentService service) =>
        PaymentEndpointSupport.ExecuteAsync(async () =>
        {
            var buyerId = PaymentEndpointSupport.RequireUserName(_http);
            var ct = PaymentEndpointSupport.RequestAborted(_http);
            // The idempotency key is caller-supplied: the same key never refunds twice, while two distinct
            // keys are two legitimate partial refunds. The service rejects a blank key.
            var idempotencyKey = request.IdempotencyKey?.Trim() ?? string.Empty;

            var refundId = await service.RefundAsync(buyerId, orderId, request.Amount, idempotencyKey, ct);
            return Results.Ok(new { refundId, orderId });
        });

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service) =>
        HandleAsync(0, request, service);
}
