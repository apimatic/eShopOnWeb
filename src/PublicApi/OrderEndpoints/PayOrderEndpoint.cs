using System.Threading.Tasks;
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

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<Order>>
{
    private readonly IPayPalService _payPal;

    public PayOrderEndpoint(IPayPalService payPal) => _payPal = payPal;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   PayOrderRequest request,
                   IRepository<Order> orderRepository,
                   IRepository<OrderPayment> paymentRepository,
                   IRepository<PaymentMethod> methodRepository,
                   HttpContext ctx) =>
            {
                var buyer = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyer))
                    return Results.Unauthorized();

                var orderSpec = new OrderWithItemsByIdSpec(orderId);
                var order = await orderRepository.FirstOrDefaultAsync(orderSpec);
                if (order == null) return Results.NotFound(new { error = "Order not found." });
                if (order.BuyerId != buyer) return Results.Forbid();

                var paymentSpec = new OrderPaymentByOrderIdSpec(orderId);
                var payment = await paymentRepository.FirstOrDefaultAsync(paymentSpec);
                if (payment == null) return Results.NotFound(new { error = "Order payment record not found." });

                // Idempotency: already authorized
                if (payment.Status is OrderPaymentStatus.Authorized
                    or OrderPaymentStatus.Captured
                    or OrderPaymentStatus.PartiallyRefunded
                    or OrderPaymentStatus.Refunded)
                {
                    return Results.Ok(new PayOrderResponse(request.CorrelationId())
                    {
                        AuthorizationId = payment.AuthorizationId,
                        PayPalOrderId = payment.PayPalOrderId,
                        Status = payment.Status.ToString()
                    });
                }

                if (payment.Status == OrderPaymentStatus.Voided)
                    return Results.BadRequest(new { error = "This order has been cancelled and cannot be paid." });

                AuthorizeResult authResult;
                try
                {
                    if (request.PaymentMethodId.HasValue)
                    {
                        // Pay with saved card
                        var methodSpec = new PaymentMethodByIdAndBuyerIdSpec(request.PaymentMethodId.Value, buyer);
                        var method = await methodRepository.FirstOrDefaultAsync(methodSpec);
                        if (method == null)
                            return Results.BadRequest(new { error = "Payment method not found or does not belong to you." });

                        authResult = await _payPal.AuthorizeOrderWithTokenAsync(
                            order.Total(), method.PayPalTokenId, ctx.RequestAborted);
                    }
                    else if (request.Card != null)
                    {
                        var card = new CardPaymentDetails(
                            Number: request.Card.Number,
                            Expiry: request.Card.Expiry,
                            SecurityCode: request.Card.SecurityCode,
                            CardholderName: request.Card.CardholderName,
                            AddressLine1: request.Card.BillingAddress?.AddressLine1 ?? string.Empty,
                            City: request.Card.BillingAddress?.City ?? string.Empty,
                            State: request.Card.BillingAddress?.State ?? string.Empty,
                            CountryCode: request.Card.BillingAddress?.CountryCode ?? "US",
                            PostalCode: request.Card.BillingAddress?.PostalCode ?? string.Empty);

                        authResult = await _payPal.AuthorizeOrderAsync(
                            order.Total(), card, ctx.RequestAborted);
                    }
                    else
                    {
                        return Results.BadRequest(new { error = "Provide either 'card' or 'paymentMethodId'." });
                    }
                }
                catch (PayPalException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }

                payment.RecordAuthorization(authResult.PayPalOrderId, authResult.AuthorizationId);
                await paymentRepository.UpdateAsync(payment);

                return Results.Ok(new PayOrderResponse(request.CorrelationId())
                {
                    AuthorizationId = authResult.AuthorizationId,
                    PayPalOrderId = authResult.PayPalOrderId,
                    Status = payment.Status.ToString()
                });
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> repository)
        => Task.FromResult(Results.StatusCode(501) as IResult);
}
