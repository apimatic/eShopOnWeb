using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<OrderPayment>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   IRepository<OrderPayment> paymentRepo,
                   IPayPalClient paypal,
                   ILogger<CancelOrderEndpoint> logger) =>
            {
                var spec = new OrderPaymentByOrderIdSpec(orderId);
                var payment = await paymentRepo.FirstOrDefaultAsync(spec);
                if (payment == null)
                    return Results.NotFound(new { error = "Order not found." });

                // Idempotency: already voided
                if (payment.Status == PaymentStatus.Voided)
                    return Results.Ok(new { status = payment.Status.ToString() });

                // Cannot cancel after capture
                if (payment.Status == PaymentStatus.Captured
                    || payment.Status == PaymentStatus.PartiallyRefunded
                    || payment.Status == PaymentStatus.Refunded)
                    return Results.UnprocessableEntity(new { error = $"Cannot cancel an order in state {payment.Status}. Use refund instead." });

                if (payment.Status == PaymentStatus.AwaitingPayment)
                {
                    // No PayPal auth to void — just mark cancelled
                    payment.SetVoided();
                    await paymentRepo.UpdateAsync(payment);
                    return Results.Ok(new { status = payment.Status.ToString() });
                }

                if (payment.Status != PaymentStatus.Authorized)
                    return Results.UnprocessableEntity(new { error = $"Order is in state {payment.Status} and cannot be cancelled." });

                try
                {
                    await paypal.VoidAuthorizationAsync(payment.AuthorizationId!);
                    payment.SetVoided();
                    await paymentRepo.UpdateAsync(payment);
                    return Results.Ok(new { status = payment.Status.ToString() });
                }
                catch (PayPalException ex)
                {
                    logger.LogError(ex, "PayPal void failed for order {OrderId}", orderId);
                    return Results.UnprocessableEntity(new { error = ex.Message, detail = ex.PayPalErrorBody });
                }
            })
            .Produces(200)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<OrderPayment> service)
        => Task.FromResult(Results.StatusCode(501));
}

public class CancelOrderRequest : BaseRequest { }
