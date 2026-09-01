using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a fulfilled order's captured payment, in full (amount omitted) or
/// in part. The caller-supplied idempotency key makes repeats safe: the same
/// key never refunds twice; distinct keys remain legitimate separate refunds.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest>
{
    private readonly IPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, ct);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request) => HandleAsync(request, CancellationToken.None);

    public async Task<IResult> HandleAsync(RefundOrderRequest request, CancellationToken ct)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "idempotencyKey is required." });
        }

        try
        {
            var (order, refund) = await _paymentService.RefundOrderAsync(buyerId, request.OrderId, request.Amount, request.IdempotencyKey, ct);
            if (order is null || refund is null)
            {
                return Results.NotFound(new { message = $"Order {request.OrderId} was not found." });
            }

            var response = new RefundOrderResponse(request.CorrelationId())
            {
                RefundId = refund.PayPalRefundId,
                OrderId = order.Id,
                Status = refund.Status,
                Amount = refund.Amount,
                Currency = order.Currency,
                TotalRefunded = order.TotalRefunded(),
                RemainingRefundable = order.RefundableAmount()
            };
            return Results.Created($"api/orders/{order.Id}/refunds/{refund.PayPalRefundId}", response);
        }
        catch (PaymentGatewayException ex)
        {
            return PaymentErrorMapper.ToErrorResult(ex);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}
