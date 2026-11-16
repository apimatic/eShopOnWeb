using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (int orderId, RefundOrderRequest request, IPaymentApplicationService payments,
                    ClaimsPrincipal user, CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var buyerId = PaymentEndpointHelpers.RequireBuyerId(user);
                        var refund = await payments.RefundAsync(
                            buyerId,
                            orderId,
                            request.AmountCents,
                            request.IdempotencyKey,
                            cancellationToken);

                        return Results.Ok(new RefundOrderResponse
                        {
                            RefundId = refund.RefundId,
                            OrderId = refund.OrderId,
                            AmountCents = refund.AmountCents,
                            TotalRefundedCents = refund.TotalRefundedCents,
                            State = refund.State
                        });
                    }
                    catch (PaymentOperationException exception)
                    {
                        return PaymentEndpointHelpers.Fail(exception);
                    }
                })
            .Produces<RefundOrderResponse>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .WithTags("PaymentEndpoints");
    }
}
