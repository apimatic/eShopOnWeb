using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDetailsRequest? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with instead of raw card details.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }
    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total, paying with a one-off card or
/// a saved card. Shopper-scoped: only the order's own buyer may pay it. Idempotent in effect.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, HttpContext>
{
    private readonly IOrderPaymentService _orders;
    private readonly PayPalSettings _settings;

    public PayOrderEndpoint(IOrderPaymentService orders, PayPalSettings settings)
    {
        _orders = orders;
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PayOrderRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, HttpContext http)
    {
        var ct = http.RequestAborted;
        var buyerId = http.BuyerId();
        var orderId = http.RouteInt("orderId");

        if (request.Card is null && !request.SavedPaymentMethodId.HasValue)
        {
            return Results.BadRequest("Provide either card details or a saved payment method id.");
        }
        if (request.Card is not null && request.SavedPaymentMethodId.HasValue)
        {
            return Results.BadRequest("Provide either card details or a saved payment method id, not both.");
        }

        var order = await _orders.AuthorizeAsync(
            buyerId, orderId, request.Card?.ToCardDetails(), request.SavedPaymentMethodId, ct);

        return Results.Ok(new PayOrderResponse(request.CorrelationId())
        {
            Order = OrderDto.From(order, _settings.Currency)
        });
    }
}
