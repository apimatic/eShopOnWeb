using System;
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
/// Operator action: returns money to the shopper after fulfilment, in full or in part.
/// A partly-refunded order can never be refunded beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService orderPaymentService, HttpContext http, CancellationToken ct) =>
            {
                return await HandleAsync(request, orderPaymentService, http, orderId, ct);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService) =>
        HandleAsync(request, orderPaymentService, httpContext: null, orderId: 0, CancellationToken.None);

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService, HttpContext? httpContext, int orderId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotencyKey is required for refunds." });
        }

        var outcome = await orderPaymentService.RefundOrderAsync(orderId, request.Amount, request.IdempotencyKey, ct);
        return Results.Ok(new RefundOrderResponse(request.CorrelationId())
        {
            OrderId = orderId,
            RefundId = outcome.Refund.Id,
            PayPalRefundId = outcome.Refund.PayPalRefundId,
            Status = outcome.Refund.Status,
            Amount = outcome.Refund.Amount,
            Currency = outcome.Refund.Currency,
            RefundedAmount = outcome.RefundedAmount,
            RefundableAmount = outcome.RefundableAmount
        });
    }
}
