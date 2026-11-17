using System;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: refunds a captured payment, in full or in part.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    throw new PaymentException(400, "An idempotency key is required for refunds.");
                }

                var amountCents = request.AmountCents
                    ?? (long)Math.Round(request.Amount * 100m, MidpointRounding.AwayFromZero);
                var result = await paymentService.RefundOrderAsync(
                    orderId, amountCents, request.IdempotencyKey, cancellationToken);

                var response = new RefundOrderResponse(request.CorrelationId())
                {
                    RefundId = result.Refund.Id,
                    OrderId = result.Payment.OrderId,
                    Amount = result.Refund.AmountCents / 100m,
                    Currency = result.Payment.Currency,
                    RefundState = result.Refund.State.ToString(),
                    FinixRefundId = result.Refund.FinixTransferId,
                    TotalRefunded = result.Payment.RefundedAmountCents / 100m,
                    RemainingRefundable = result.Payment.RefundableAmountCents / 100m
                };

                return Results.Ok(response);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
