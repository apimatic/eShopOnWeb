using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total with PayPal — a hold on the money, not a capture.
/// Pays either with one-off card details or with one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null)
                {
                    return Results.Unauthorized();
                }

                var hasCard = request.Card is not null;
                var hasSavedCard = request.SavedPaymentMethodId.HasValue;
                if (hasCard == hasSavedCard)
                {
                    return Results.BadRequest("Supply exactly one of 'card' or 'savedPaymentMethodId'.");
                }

                CardDetails? card = null;
                if (hasCard)
                {
                    if (string.IsNullOrWhiteSpace(request.Card!.Number) ||
                        string.IsNullOrWhiteSpace(request.Card.Expiry) ||
                        string.IsNullOrWhiteSpace(request.Card.SecurityCode))
                    {
                        return Results.BadRequest("Card number, expiry and securityCode are required.");
                    }

                    card = new CardDetails(
                        request.Card.Number,
                        request.Card.Expiry,
                        request.Card.SecurityCode!,
                        request.Card.Name,
                        request.Card.BillingAddress?.AddressLine1,
                        request.Card.BillingAddress?.City,
                        request.Card.BillingAddress?.State,
                        request.Card.BillingAddress?.PostalCode,
                        request.Card.BillingAddress?.CountryCode);
                }

                var payment = await orderPaymentService.PayOrderAsync(buyerId, orderId, card, request.SavedPaymentMethodId, ct);
                if (payment is null)
                {
                    return Results.NotFound();
                }

                var response = new PayOrderResponse(request.CorrelationId())
                {
                    OrderId = orderId,
                    PaymentId = payment.Id,
                    Status = payment.Status.ToString(),
                    AuthorizationId = payment.AuthorizationId,
                    Amount = payment.Amount,
                    Currency = payment.Currency,
                    ExpiresAt = payment.AuthorizationExpiresAt
                };
                return Results.Ok(response);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
