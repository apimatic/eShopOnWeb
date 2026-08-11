using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/refunds — refunds a captured order, full or partial, under a
/// caller-supplied idempotency key. Shopper-scoped to the caller's own order. Returns the refund id
/// as a top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService service, HttpContext http) =>
            {
                request.OrderId = orderId;
                request.BuyerId = http.BuyerId();
                // Accept the idempotency key from a header too, if the body did not carry one.
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
                    http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    request.IdempotencyKey = header.ToString();
                }
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ValidationException("A refund idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header).");
        }

        var (order, refund) = await service.RefundAsync(request.OrderId, request.BuyerId, request.Amount, request.IdempotencyKey);

        var response = new RefundOrderResponse
        {
            RefundId = refund.PayPalRefundId,
            OrderId = order.Id,
            Amount = refund.Amount,
            Status = refund.Status,
            RefundedAmount = order.Payment!.RefundedAmount,
            RefundableRemaining = order.Payment!.RefundableRemaining
        };
        return Results.Created($"api/orders/{order.Id}/refunds/{refund.PayPalRefundId}", response);
    }
}

/// <summary>Response for a refund, carrying the refund id as a top-level field.</summary>
public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal RefundedAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
}
