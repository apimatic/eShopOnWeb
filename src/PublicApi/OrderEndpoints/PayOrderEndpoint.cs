using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Omit when paying with a saved card.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards. Omit when paying with fresh card details.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }
}

/// <summary>
/// Authorizes (holds) the order total. The money is held, not taken — capture happens at fulfilment.
/// Pays with either fresh card details or one of the caller's saved cards. Idempotent in effect.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();
                request.OrderId = orderId;
                request.BuyerId = buyerId;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var instruction = new PaymentInstruction
        {
            SavedPaymentMethodId = request.SavedPaymentMethodId,
            Card = request.Card is null ? null : new CardDetails(
                request.Card.Number,
                request.Card.ExpiryMonth,
                request.Card.ExpiryYear,
                request.Card.SecurityCode,
                request.Card.CardholderName,
                request.Card.BillingAddress is null ? null : new BillingAddress(
                    request.Card.BillingAddress.AddressLine1,
                    request.Card.BillingAddress.AddressLine2,
                    request.Card.BillingAddress.City,
                    request.Card.BillingAddress.State,
                    request.Card.BillingAddress.PostalCode,
                    request.Card.BillingAddress.CountryCode))
        };

        var order = await orderPaymentService.AuthorizeAsync(request.OrderId, request.BuyerId!, instruction);
        return Results.Ok(OrderPaymentMapper.ToDto(order));
    }
}
