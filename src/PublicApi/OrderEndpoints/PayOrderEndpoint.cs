using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<PaymentRecord>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   PayOrderRequest request,
                   IRepository<Order> orderRepo,
                   IRepository<PaymentRecord> paymentRepo,
                   IRepository<SavedPaymentMethod> methodRepo,
                   IPayPalService payPal,
                   IConfiguration config,
                   HttpContext ctx,
                   CancellationToken ct) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var orderSpec = new OrderByIdAndBuyerSpec(orderId, buyerId);
                var order = await orderRepo.FirstOrDefaultAsync(orderSpec, ct);
                if (order == null)
                    return Results.NotFound(new { error = "Order not found." });

                var paySpec = new PaymentRecordByOrderAndBuyerSpec(orderId, buyerId);
                var payment = await paymentRepo.FirstOrDefaultAsync(paySpec, ct);
                if (payment == null)
                    return Results.NotFound(new { error = "Payment record not found." });

                if (payment.Status == PaymentStatus.Authorized)
                    return Results.Ok(new PayOrderResponse
                    {
                        AuthorizationId = payment.AuthorizationId!,
                        Status = payment.Status,
                        PayPalOrderId = payment.PayPalOrderId!
                    });

                if (payment.Status != PaymentStatus.PendingPayment)
                    return Results.Conflict(new { error = $"Order cannot be paid in state '{payment.Status}'." });

                var currency = config["PayPal:Currency"] ?? "USD";
                var idempotencyKey = payment.PaymentIdempotencyKey ?? $"pay-order-{orderId}";
                var amount = order.Total();

                PayPalAuthorizeResult authResult;
                try
                {
                    if (string.Equals(request.Type, "savedCard", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!request.PaymentMethodId.HasValue)
                            return Results.BadRequest(new { error = "paymentMethodId is required for savedCard payment." });

                        var methodSpec = new SavedPaymentMethodByIdAndBuyerSpec(request.PaymentMethodId.Value, buyerId);
                        var method = await methodRepo.FirstOrDefaultAsync(methodSpec, ct);
                        if (method == null)
                            return Results.NotFound(new { error = "Payment method not found or does not belong to you." });

                        authResult = await payPal.AuthorizeWithVaultTokenAsync(amount, currency, method.PaymentTokenId, idempotencyKey, ct);
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(request.CardNumber) || string.IsNullOrEmpty(request.CardExpiry) || string.IsNullOrEmpty(request.CardCvv))
                            return Results.BadRequest(new { error = "CardNumber, CardExpiry (YYYY-MM), and CardCvv are required." });

                        var card = new PayPalCardDetails
                        {
                            Number = request.CardNumber,
                            Expiry = request.CardExpiry,
                            SecurityCode = request.CardCvv,
                            CardholderName = request.CardholderName
                        };

                        authResult = await payPal.AuthorizeWithCardAsync(amount, currency, card, idempotencyKey, ct);
                    }
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode ?? 502, title: "Payment authorization failed.");
                }

                payment.SetAuthorized(authResult.PayPalOrderId, authResult.AuthorizationId, authResult.AuthorizationStatus, idempotencyKey);
                await paymentRepo.UpdateAsync(payment, ct);

                return Results.Ok(new PayOrderResponse
                {
                    AuthorizationId = authResult.AuthorizationId,
                    Status = payment.Status,
                    PayPalOrderId = authResult.PayPalOrderId
                });
            })
            .Produces<PayOrderResponse>()
            .Produces(400)
            .Produces(404)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IRepository<PaymentRecord> service)
        => throw new NotImplementedException();
}
