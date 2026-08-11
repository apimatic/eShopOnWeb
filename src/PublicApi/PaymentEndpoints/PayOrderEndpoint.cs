using System.Security.Claims;
using System.Text.Json.Serialization;
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

public class PayOrderRequest
{
    /// <summary>Raw card details for a one-off payment. Provide this or <see cref="PaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>The id of one of the shopper's saved cards to pay with instead.</summary>
    public int? PaymentMethodId { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

/// <summary>
/// Authorizes an order's total: places a hold on the money without taking it. The request either carries
/// card details for a one-off payment, or names one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest>
{
    private readonly IPaymentService _paymentService;

    public PayOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request);
            })
            .Produces<PaymentDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request)
    {
        if (request.Card is null && !request.PaymentMethodId.HasValue)
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId to pay with.");
        }

        var instrument = new PaymentInstrument(request.Card?.ToCardDetails(), request.PaymentMethodId);
        var payment = await _paymentService.AuthorizeOrderAsync(request.BuyerId, request.OrderId, instrument);

        return Results.Ok(PaymentDto.From(payment));
    }
}
