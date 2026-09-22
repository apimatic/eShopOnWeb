using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/refunds — refund the captured payment for the caller's own order, in full or
/// in part. Carries a caller-supplied idempotency key: repeating under the same key does not refund twice,
/// while two distinct partial refunds remain legitimate. Never refundable beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext http) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, http);
            })
            .Produces<RefundResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, HttpContext http)
    {
        try
        {
            var buyerId = PaymentEndpointHelpers.GetBuyerId(http);
            var svc = http.RequestServices.GetRequiredService<IPaymentOrchestrationService>();

            // Idempotency key: request body, falling back to an Idempotency-Key header.
            var idempotencyKey = request.IdempotencyKey;
            if (string.IsNullOrWhiteSpace(idempotencyKey) && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                idempotencyKey = header.ToString();

            if (string.IsNullOrWhiteSpace(idempotencyKey))
                return PaymentEndpointHelpers.MapError(new PaymentOperationException(
                    PaymentOperationErrorKind.Validation, "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)."));

            var result = await svc.RefundAsync(buyerId, request.OrderId, request.Amount, idempotencyKey!, http.RequestAborted);
            return Results.Ok(new RefundResponse
            {
                RefundId = result.RefundId,
                Amount = result.Amount,
                Status = result.Status,
                Payment = result.Payment,
            });
        }
        catch (Exception ex) when (ex is PaymentOperationException or PaymentGatewayException)
        {
            return PaymentEndpointHelpers.MapError(ex);
        }
    }
}

public class RefundOrderRequest
{
    public int OrderId { get; set; }
    /// <summary>Partial refund amount; omit for a full refund of the remaining refundable amount.</summary>
    public decimal? Amount { get; set; }
    /// <summary>Caller-supplied idempotency key (or send the Idempotency-Key header).</summary>
    public string? IdempotencyKey { get; set; }
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Status { get; set; }
    public PaymentView? Payment { get; set; }
}
