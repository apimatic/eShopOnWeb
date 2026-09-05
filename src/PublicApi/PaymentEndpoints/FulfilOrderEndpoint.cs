using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using BlazorShared.Authorization;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: marks the order fulfilled and takes the money by capturing the
/// authorization. A stale authorization is renewed first; one that cannot be renewed
/// is reported in actionable terms.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, FulfilOrderRequest request, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IPaymentService paymentService)
    {
        var result = await paymentService.FulfilOrderAsync(request.OrderId, default);
        if (!result.Succeeded)
        {
            return PaymentEndpointHelpers.FromError(result.Error!);
        }

        var response = new FulfilOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Status = "Fulfilled",
            Payment = ToPaymentState(result.Payment!)
        };

        return Results.Ok(response);
    }

    internal static PaymentStateDto ToPaymentState(ApplicationCore.Entities.PaymentAggregate.Payment payment) =>
        new PaymentStateDto
        {
            PaymentId = payment.Id,
            State = payment.Status.ToString(),
            Amount = payment.Amount,
            Currency = payment.Currency,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.PayPalAuthorizationId,
            AuthorizationStatus = payment.PayPalAuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.PayPalCaptureId,
            CaptureStatus = payment.PayPalCaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            CapturedAt = payment.CapturedAt,
            RefundedAmount = payment.RefundedAmountCommitted,
            Refunds = payment.Refunds.Select(r => new PaymentRefundDto
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Status = r.Status,
                Amount = r.Amount,
                Currency = r.Currency,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
}



