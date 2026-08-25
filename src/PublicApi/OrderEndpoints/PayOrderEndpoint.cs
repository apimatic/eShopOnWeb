using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest
{
    // One of the two must be provided
    public int? PaymentMethodId { get; set; }

    // Card details (required if PaymentMethodId is null)
    public string? CardNumber { get; set; }
    public string? Expiry { get; set; }
    public string? Cvv { get; set; }
    public string? CardholderName { get; set; }
    public AddressRequest? BillingAddress { get; set; }
}

public class PayOrderResponse
{
    public string AuthorizationId { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
    public string PaymentStatus { get; set; } = "";
}

public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, HttpContext ctx,
                   IRepository<Order> orderRepo,
                   IRepository<SavedPaymentMethod> pmRepo,
                   PayPalClient paypal,
                   IOptions<PayPalSettings> settings) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var spec = new OrderWithPaymentSpec(orderId);
                var order = await orderRepo.GetBySpecAsync(spec);
                if (order == null || order.BuyerId != buyerId)
                    return Results.NotFound(new { error = "Order not found." });

                // Idempotency: already authorized → return current state
                if (order.PaymentStatus == PaymentStatus.Authorized)
                    return Results.Ok(new PayOrderResponse
                    {
                        AuthorizationId = order.PayPalAuthorizationId ?? "",
                        ExpiresAt = order.AuthorizationExpiresAt?.ToString("O") ?? "",
                        PaymentStatus = order.PaymentStatus.ToString()
                    });

                if (order.PaymentStatus != PaymentStatus.PendingPayment)
                    return Results.UnprocessableEntity(new
                    {
                        error = $"Order cannot be paid in its current state: {order.PaymentStatus}."
                    });

                var currency = settings.Value.Currency;
                var amount = order.Total();

                // Per-order UUID idempotency key — stable within a session, unique across restarts
                var idempotencyKey = order.PayIdempotencyKey;

                Infrastructure.PayPal.PayPalAuthorizationResult authResult;

                try
                {
                    if (request.PaymentMethodId.HasValue)
                    {
                        // Pay with saved card
                        var pmSpec = new SavedPaymentMethodByIdAndBuyerSpec(request.PaymentMethodId.Value, buyerId);
                        var pm = await pmRepo.GetBySpecAsync(pmSpec);
                        if (pm == null)
                            return Results.NotFound(new { error = "Payment method not found." });

                        authResult = await paypal.AuthorizeWithVaultAsync(amount, currency, pm.PayPalVaultId, idempotencyKey);
                    }
                    else
                    {
                        // Pay with new card
                        if (string.IsNullOrEmpty(request.CardNumber) ||
                            string.IsNullOrEmpty(request.Expiry) ||
                            string.IsNullOrEmpty(request.Cvv) ||
                            string.IsNullOrEmpty(request.CardholderName) ||
                            request.BillingAddress == null)
                        {
                            return Results.BadRequest(new
                            {
                                error = "Provide either paymentMethodId or full card details (cardNumber, expiry, cvv, cardholderName, billingAddress)."
                            });
                        }

                        var addr = request.BillingAddress;
                        authResult = await paypal.AuthorizeWithCardAsync(
                            amount, currency,
                            request.CardNumber, request.Expiry, request.Cvv,
                            request.CardholderName,
                            addr.Street, addr.City, addr.State, addr.Country, addr.ZipCode,
                            idempotencyKey);
                    }
                }
                catch (PayerActionRequiredException ex)
                {
                    return Results.UnprocessableEntity(new { error = ex.Message });
                }
                catch (PayPalException ex)
                {
                    return Results.UnprocessableEntity(new { error = ex.Message, paypalCode = ex.PayPalName });
                }

                order.MarkAuthorized(authResult.OrderId, authResult.AuthorizationId, authResult.ExpiresAt);
                await orderRepo.UpdateAsync(order);

                return Results.Ok(new PayOrderResponse
                {
                    AuthorizationId = authResult.AuthorizationId,
                    ExpiresAt = authResult.ExpiresAt.ToString("O"),
                    PaymentStatus = order.PaymentStatus.ToString()
                });
            })
            .Produces<PayOrderResponse>()
            .ProducesProblem(400)
            .ProducesProblem(422)
            .WithTags("OrderEndpoints");
    }
}
