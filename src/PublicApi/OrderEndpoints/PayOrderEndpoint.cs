using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<OrderPayment>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   PayOrderRequest request,
                   HttpContext httpContext,
                   IReadRepository<Order> orderRepo,
                   IRepository<OrderPayment> paymentRepo,
                   IReadRepository<UserPaymentMethod> pmRepo,
                   IPayPalClient paypal,
                   ILogger<PayOrderEndpoint> logger) =>
            {
                var userName = httpContext.User.Identity!.Name!;

                // Validate exactly one payment method provided
                bool hasCard = !string.IsNullOrWhiteSpace(request.CardNumber);
                bool hasVault = request.SavedCardId.HasValue;
                if (hasCard == hasVault)
                    return Results.BadRequest(new { error = "Provide either card details or a savedCardId, not both." });

                // Load order and verify ownership
                var order = await orderRepo.GetByIdAsync(orderId);
                if (order == null || order.BuyerId != userName)
                    return Results.NotFound(new { error = "Order not found." });

                // Load or create payment record
                var paymentSpec = new OrderPaymentByOrderIdSpec(orderId);
                var payment = await paymentRepo.FirstOrDefaultAsync(paymentSpec);
                if (payment == null)
                    return Results.UnprocessableEntity(new { error = "No payment record for this order." });

                // Idempotency: already authorized
                if (payment.AuthorizationId != null)
                    return Results.Ok(new PayOrderResponse(payment.Status.ToString(), payment.AuthorizationId));

                // Wrong state
                if (payment.Status != PaymentStatus.AwaitingPayment)
                    return Results.UnprocessableEntity(new { error = $"Order payment is in state {payment.Status} and cannot be authorized." });

                // Build card source
                PayPalCardSource cardSource;
                if (hasVault)
                {
                    var pmSpec = new UserPaymentMethodByIdAndUserIdSpec(request.SavedCardId!.Value, userName);
                    var pm = await pmRepo.FirstOrDefaultAsync(pmSpec);
                    if (pm == null)
                        return Results.NotFound(new { error = "Saved card not found." });
                    cardSource = new PayPalCardSource(VaultId: pm.PaymentTokenId);
                }
                else
                {
                    cardSource = new PayPalCardSource(
                        Number: request.CardNumber,
                        Expiry: request.CardExpiry,
                        SecurityCode: request.CardCvv,
                        Name: request.CardName,
                        BillingAddress: request.BillingCountry != null
                            ? new PayPalAddress(request.BillingCountry)
                            : null
                    );
                }

                try
                {
                    // Step 1: create PayPal order if not already done
                    if (payment.PayPalOrderId == null)
                    {
                        var createKey = $"eshop-create-p{payment.Id}";
                        var ppOrder = await paypal.CreateOrderAsync(
                            payment.Amount.ToString("F2"),
                            payment.Currency,
                            $"eshop-order-{orderId}",
                            createKey);
                        payment.SetPayPalOrderCreated(ppOrder.Id);
                        await paymentRepo.UpdateAsync(payment);
                    }

                    // Step 2: authorize
                    var authKey = $"eshop-auth-p{payment.Id}";
                    var authResult = await paypal.AuthorizeOrderAsync(payment.PayPalOrderId!, cardSource, authKey);

                    var authId = authResult.PurchaseUnits?[0]?.Payments?.Authorizations?[0]?.Id;
                    if (string.IsNullOrEmpty(authId))
                    {
                        logger.LogError("PayPal authorize returned no authorization ID for order {OrderId}", orderId);
                        return Results.UnprocessableEntity(new { error = "Authorization did not return an ID. Order status: " + authResult.Status });
                    }

                    payment.SetAuthorized(authId);
                    await paymentRepo.UpdateAsync(payment);

                    return Results.Ok(new PayOrderResponse(payment.Status.ToString(), authId));
                }
                catch (PayPalChallengeRequiredException ex)
                {
                    return Results.UnprocessableEntity(new { error = ex.Message });
                }
                catch (PayPalException ex)
                {
                    logger.LogError(ex, "PayPal error during pay for order {OrderId}", orderId);
                    return Results.UnprocessableEntity(new { error = ex.Message, detail = ex.PayPalErrorBody });
                }
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IRepository<OrderPayment> service)
        => Task.FromResult(Results.StatusCode(501));
}

public class PayOrderRequest : BaseRequest
{
    // One-off card payment
    public string? CardNumber { get; set; }
    public string? CardExpiry { get; set; }
    public string? CardCvv { get; set; }
    public string? CardName { get; set; }
    public string? BillingCountry { get; set; }

    // Saved card payment
    public int? SavedCardId { get; set; }
}

public record PayOrderResponse(string Status, string AuthorizationId);
