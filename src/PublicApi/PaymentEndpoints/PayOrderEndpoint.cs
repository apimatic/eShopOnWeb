using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Card details for a one-off payment. Supply this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with. Supply this OR <see cref="Card"/>.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public OrderView Order { get; set; } = default!;
}

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total. Idempotent in effect:
/// a double-click never authorizes twice. The money is held, not taken.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService, PayPalConfiguration>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user,
             IOrderPaymentService service, PayPalConfiguration config) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                request.OrderId = orderId;
                request.BuyerId = buyerId;
                return await HandleAsync(request, service, config);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service, PayPalConfiguration config)
    {
        var card = request.Card?.ToGatewayCard();
        var order = await service.AuthorizeOrderAsync(request.BuyerId, request.OrderId, card, request.SavedPaymentMethodId);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderView.From(order, config.Currency)
        };
        return Results.Ok(response);
    }
}
