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
/// Operator: refunds the captured payment, in full or in part. A partly-refunded order can
/// never become refundable beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService orderService, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, orderService, ct);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderService)
    {
        return HandleAsync(request, orderService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderService, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new PaymentErrorResponse
            {
                StatusCode = 400,
                Message = "idempotencyKey is required."
            });
        }

        try
        {
            var outcome = await orderService.RefundOrderAsync(request.OrderId, request.Amount,
                request.IdempotencyKey, ct);

            var response = new RefundOrderResponse(request.CorrelationId())
            {
                RefundId = outcome.Refund.PayPalRefundId,
                OrderId = outcome.Order.Id,
                Status = outcome.Refund.Status,
                Amount = outcome.Refund.Amount,
                Currency = outcome.Refund.Currency,
                TotalRefunded = outcome.Order.TotalRefunded,
                RemainingRefundable = outcome.Order.RemainingRefundable,
                Replayed = outcome.Replayed,
                PaymentStatus = outcome.Order.PaymentStatus.ToString()
            };
            return Results.Created($"api/orders/{outcome.Order.Id}/refunds/{outcome.Refund.PayPalRefundId}", response);
        }
        catch (Exception ex) when (EndpointErrorMapper.TryMap(ex, out var error))
        {
            return error;
        }
    }
}
