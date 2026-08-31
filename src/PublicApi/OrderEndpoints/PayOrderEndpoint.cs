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
/// Authorizes (holds) the order total - with raw card details or a saved card.
/// The money is not taken until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, int, PayOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService orderPaymentService) =>
            {
                return await HandleAsync(orderId, request, orderPaymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

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

        var payment = await orderPaymentService.AuthorizeAsync(buyerId, orderId, card, request.SavedCardId);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = orderId,
            OrderStatus = "Authorized",
            PaymentStatus = payment.Status.ToString(),
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizedAmount = payment.AuthorizedAmount,
            Currency = payment.Currency,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt
        };
        return Results.Ok(response);
    }
}
