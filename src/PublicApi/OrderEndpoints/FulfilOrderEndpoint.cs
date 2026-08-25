using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IRepository<Order>>
{
    private readonly IPayPalService _payPal;

    public FulfilOrderEndpoint(IPayPalService payPal) => _payPal = payPal;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   IRepository<Order> orderRepository,
                   IRepository<OrderPayment> paymentRepository,
                   HttpContext ctx) =>
            {
                var orderSpec = new OrderWithItemsByIdSpec(orderId);
                var order = await orderRepository.FirstOrDefaultAsync(orderSpec);
                if (order == null) return Results.NotFound(new { error = "Order not found." });

                var paymentSpec = new OrderPaymentByOrderIdSpec(orderId);
                var payment = await paymentRepository.FirstOrDefaultAsync(paymentSpec);
                if (payment == null) return Results.NotFound(new { error = "Payment record not found." });

                if (payment.Status != OrderPaymentStatus.Authorized)
                    return Results.BadRequest(new { error = $"Order is not in Authorized state (current: {payment.Status})." });

                if (string.IsNullOrEmpty(payment.AuthorizationId))
                    return Results.BadRequest(new { error = "No authorization ID on record." });

                CaptureResult captureResult;
                try
                {
                    captureResult = await _payPal.CaptureAsync(payment.AuthorizationId, ctx.RequestAborted);
                }
                catch (PayPalException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }

                if (captureResult.NewAuthorizationId != null)
                    payment.UpdateAuthorizationId(captureResult.NewAuthorizationId);

                payment.RecordCapture(
                    captureResult.CaptureId,
                    captureResult.CapturedAmount,
                    captureResult.Fee,
                    captureResult.Net);

                await paymentRepository.UpdateAsync(payment);

                return Results.Ok(new
                {
                    captureId = captureResult.CaptureId,
                    capturedAmount = captureResult.CapturedAmount,
                    payPalFee = captureResult.Fee,
                    netAmount = captureResult.Net,
                    currency = _payPal.Currency
                });
            })
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request, IRepository<Order> repository)
        => Task.FromResult(Results.StatusCode(501) as IResult);
}
