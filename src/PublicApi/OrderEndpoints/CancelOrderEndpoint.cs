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

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<Order>>
{
    private readonly IPayPalService _payPal;

    public CancelOrderEndpoint(IPayPalService payPal) => _payPal = payPal;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
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

                if (payment.Status == OrderPaymentStatus.Captured
                    || payment.Status == OrderPaymentStatus.Refunded
                    || payment.Status == OrderPaymentStatus.PartiallyRefunded)
                {
                    return Results.BadRequest(new { error = "Cannot cancel an already fulfilled order. Use the refund endpoint instead." });
                }

                if (payment.Status == OrderPaymentStatus.Voided)
                    return Results.Ok(new { message = "Order was already cancelled." });

                if (payment.Status == OrderPaymentStatus.Authorized
                    && !string.IsNullOrEmpty(payment.AuthorizationId))
                {
                    try
                    {
                        await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, ctx.RequestAborted);
                    }
                    catch (PayPalException ex)
                    {
                        return Results.BadRequest(new { error = ex.Message });
                    }
                }

                payment.RecordVoid();
                await paymentRepository.UpdateAsync(payment);

                return Results.Ok(new { message = "Order cancelled. Any held funds have been released." });
            })
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> repository)
        => Task.FromResult(Results.StatusCode(501) as IResult);
}
