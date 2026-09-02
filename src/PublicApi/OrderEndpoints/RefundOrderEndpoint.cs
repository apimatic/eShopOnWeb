using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: refunds a fulfilled order's captured payment, in full or in part.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest>
{
    private readonly OrderPaymentService _paymentService;

    public RefundOrderEndpoint(OrderPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await Handle(request, ct);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request)
        => Handle(request, CancellationToken.None);

    private async Task<IResult> Handle(RefundOrderRequest request, CancellationToken ct)
    {
        try
        {
            var (order, refund, _) = await _paymentService.RefundAsync(
                request.OrderId, request.Amount, request.IdempotencyKey, ct);
            var payment = order.Payment!;
            return Results.Ok(new RefundOrderResponse
            {
                RefundId = refund.RefundId,
                OrderId = order.Id,
                Status = refund.Status,
                Amount = refund.Amount,
                TotalRefunded = payment.TotalRefunded,
                RemainingRefundable = payment.RefundableAmount,
                Currency = payment.Currency
            });
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or PaymentGatewayException)
        {
            return ApiErrorResults.FromException(ex);
        }
    }
}
