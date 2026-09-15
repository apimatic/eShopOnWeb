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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead of raw card details.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }

    public PaymentStateDto Payment { get; set; } = new();
}

/// <summary>
/// POST /api/orders/{orderId}/pay — authorize the order total (place a hold, do not take the money yet)
/// using card details or a saved card. Shopper-scoped and idempotent in effect.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                PayOrderRequest request,
                ClaimsPrincipal user,
                IOrderPaymentService service,
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);

                var hasCard = request.Card is not null;
                var hasSaved = request.SavedPaymentMethodId.HasValue;
                if (hasCard == hasSaved)
                {
                    throw new OrderValidationException(
                        "Provide exactly one of 'card' (raw card details) or 'savedPaymentMethodId'.");
                }

                var payment = await service.AuthorizeAsync(
                    buyerId, orderId, request.Card?.ToCardPaymentDetails(), request.SavedPaymentMethodId, ct);

                var response = new PayOrderResponse(request.CorrelationId())
                {
                    Payment = PaymentStateDto.From(payment)
                };
                return Results.Ok(response);
            })
            .Produces<PayOrderResponse>()
            .WithTags("PaymentEndpoints");
    }
}
