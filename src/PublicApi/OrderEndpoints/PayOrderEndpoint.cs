using System.Security.Claims;
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
/// Authorizes the order total at PayPal (a hold, not a capture), either with card details
/// supplied inline or with one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public PayOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity!.Name!;

        GatewayCardDetails? card = null;
        if (request.Card is not null)
        {
            card = new GatewayCardDetails(
                request.Card.Number,
                request.Card.Expiry,
                request.Card.SecurityCode,
                request.Card.Name,
                request.Card.BillingAddress is null
                    ? null
                    : new GatewayBillingAddress(
                        request.Card.BillingAddress.AddressLine1,
                        request.Card.BillingAddress.AddressLine2,
                        request.Card.BillingAddress.City,
                        request.Card.BillingAddress.State,
                        request.Card.BillingAddress.PostalCode,
                        request.Card.BillingAddress.CountryCode));
        }

        var payment = await _orderPaymentService.PayOrderAsync(
            request.OrderId, buyerId, card, request.PaymentMethodId);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            PaymentId = payment.Id,
            Status = payment.AuthorizationStatus ?? string.Empty,
            AuthorizationId = payment.AuthorizationId,
            AuthorizedAmount = payment.AuthorizedAmount,
            Currency = payment.Currency,
            ExpiresAt = payment.AuthorizationExpiresAt
        };
        return Results.Ok(response);
    }
}
