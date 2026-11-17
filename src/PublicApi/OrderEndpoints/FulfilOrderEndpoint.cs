using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: captures the authorized payment for a fulfilled order.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var payment = await paymentService.FulfilOrderAsync(orderId, cancellationToken);
                var response = new FulfilOrderResponse(System.Guid.NewGuid())
                {
                    OrderId = payment.OrderId,
                    PaymentStatus = payment.Status.ToString(),
                    CaptureTransferId = payment.CaptureTransferId,
                    CaptureTransferState = payment.CaptureTransferState,
                    CapturedAmount = (payment.CapturedAmountCents ?? 0) / 100m,
                    FinixFee = (payment.FinixFeeCents ?? 0) / 100m,
                    NetProceeds = (payment.NetAmountCents ?? 0) / 100m,
                    Currency = payment.Currency
                };

                return Results.Ok(response);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
