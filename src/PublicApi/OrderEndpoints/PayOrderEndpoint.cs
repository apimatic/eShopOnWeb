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
/// Authorizes the order total (a hold on the money) using either card details
/// for a one-off payment or one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal, CancellationToken>
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
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.Identity?.Name ?? string.Empty;
        var card = request.Card == null
            ? null
            : new CardDetails(request.Card.Number, request.Card.Expiry, request.Card.SecurityCode,
                request.Card.Name, request.Card.AddressLine1, request.Card.City, request.Card.State,
                request.Card.PostalCode, request.Card.CountryCode);

        var order = await _orderPaymentService.PayOrderAsync(buyerId, request.OrderId, card, request.PaymentMethodId, ct);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Payment = order.Payment == null ? null : OrderDtoMapper.ToDto(order.Payment)
        };
        return Results.Ok(response);
    }
}
