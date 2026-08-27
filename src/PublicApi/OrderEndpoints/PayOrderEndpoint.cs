using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }

    /// <summary>Card details for a one-off payment.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards, to pay with instead of raw card details.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public PaymentDto? Payment { get; set; }
}

/// <summary>
/// Authorizes (holds) the order total on a card — either raw card details or one of the
/// caller's saved cards. No money moves until fulfilment. Repeating the call replays the
/// existing authorization instead of holding the money twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        if (request.BuyerId == null)
        {
            return Results.Unauthorized();
        }
        if (request.Card == null && request.SavedPaymentMethodId == null)
        {
            return Results.BadRequest("Payment requires either card details or a savedPaymentMethodId.");
        }
        if (request.Card != null && string.IsNullOrWhiteSpace(request.Card.Number))
        {
            return Results.BadRequest("Card number is required.");
        }

        var payment = await paymentService.PayAsync(
            request.BuyerId, request.OrderId, request.Card?.ToCardDetails(), request.SavedPaymentMethodId, default);

        if (payment == null)
        {
            return Results.NotFound();
        }

        response.OrderId = request.OrderId;
        response.Payment = OrderMapping.ToDto(payment);
        return Results.Ok(response);
    }
}
