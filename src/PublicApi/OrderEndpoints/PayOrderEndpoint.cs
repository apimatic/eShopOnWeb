using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total using either a one-off card or a saved payment method.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(buyerId))
                {
                    throw new PaymentException(401, "A signed-in shopper is required.");
                }

                PaymentCard? card = null;
                var cardSource = request.Card;
                if (cardSource is not null)
                {
                    card = new PaymentCard
                    {
                        Number = cardSource.CardNumber ?? cardSource.Number ?? string.Empty,
                        ExpirationMonth = cardSource.ExpirationMonth != 0
                            ? cardSource.ExpirationMonth
                            : cardSource.ExpiryMonth ?? 0,
                        ExpirationYear = cardSource.ExpirationYear != 0
                            ? cardSource.ExpirationYear
                            : cardSource.ExpiryYear ?? 0,
                        SecurityCode = cardSource.SecurityCode ?? cardSource.Cvv ?? cardSource.Cvc,
                        Name = cardSource.Name,
                        Address = cardSource.Address
                    };
                }
                else if (!string.IsNullOrWhiteSpace(request.CardNumber)
                         || !string.IsNullOrWhiteSpace(request.Number)
                         || request.ExpirationMonth != 0
                         || request.ExpirationYear != 0)
                {
                    card = new PaymentCard
                    {
                        Number = request.CardNumber ?? request.Number ?? string.Empty,
                        ExpirationMonth = request.ExpirationMonth != 0
                            ? request.ExpirationMonth
                            : request.ExpiryMonth ?? 0,
                        ExpirationYear = request.ExpirationYear != 0
                            ? request.ExpirationYear
                            : request.ExpiryYear ?? 0,
                        SecurityCode = request.SecurityCode ?? request.Cvv ?? request.Cvc,
                        Name = request.Name,
                        Address = request.Address
                    };
                }

                var savedPaymentMethodId = request.PaymentMethodId ?? request.SavedPaymentMethodId;
                var payment = await paymentService.PayOrderAsync(
                    buyerId, orderId, savedPaymentMethodId, card, cancellationToken);

                var response = new PayOrderResponse(request.CorrelationId())
                {
                    OrderId = payment.OrderId,
                    PaymentStatus = payment.Status.ToString(),
                    AuthorizationId = payment.AuthorizationId,
                    AuthorizationState = payment.AuthorizationState,
                    AmountAuthorized = payment.AmountCents / 100m,
                    Currency = payment.Currency,
                    PaymentInstrumentId = payment.PaymentInstrumentId,
                    SavedPaymentMethodId = savedPaymentMethodId
                };

                return Results.Ok(response);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
