using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    /// <summary>Amount to refund; null refunds all that remains refundable on the capture.</summary>
    public decimal? Amount { get; set; }
    /// <summary>Caller-supplied idempotency key; repeating under the same key never refunds twice.</summary>
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public OrderPaymentDto Order { get; set; } = new();
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — refunds a captured payment, in full or in part.
/// Administrator only; idempotent per idempotency key. Returns refundId as a top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext http, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
                    http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    request.IdempotencyKey = header.ToString();
                }
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new
            {
                message = "A refund requires a caller-supplied idempotency key " +
                    "(request body 'idempotencyKey' or 'Idempotency-Key' header)."
            });
        }

        var (refund, view) = await paymentService.RefundOrderAsync(
            request.OrderId, request.Amount, request.IdempotencyKey!);

        return Results.Ok(new RefundOrderResponse
        {
            RefundId = refund.RefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            Order = PaymentMapping.ToDto(view)
        });
    }
}
