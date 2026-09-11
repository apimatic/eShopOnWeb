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
    /// <summary>Set from the route, not the request body.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Card details for a one-off payment. Provide this OR <see cref="PaymentMethodId"/>.</summary>
    public CardInputDto? Card { get; set; }

    /// <summary>A saved card id to pay with. Provide this OR <see cref="Card"/>.</summary>
    public int? PaymentMethodId { get; set; }
}

/// <summary>
/// Authorizes (places a hold on) the order total. Does not take the money — that happens at
/// fulfilment. Shopper-scoped and idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, service);
            })
            .Produces<PaymentStateDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user,
        IOrderPaymentService service)
    {
        var buyerId = CallerIdentity.GetBuyerId(user);

        CardDetails? card = null;
        if (request.Card is not null)
        {
            var c = request.Card;
            var a = c.BillingAddress;
            card = new CardDetails(c.Number, c.Expiry, c.SecurityCode, c.Name,
                a?.AddressLine1, a?.AddressLine2, a?.AdminArea2, a?.AdminArea1, a?.PostalCode, a?.CountryCode);
        }

        var instruction = new PayInstruction(card, request.PaymentMethodId);
        var payment = await service.AuthorizeAsync(buyerId, request.OrderId, instruction);

        return Results.Ok(PaymentStateDto.From(payment));
    }
}
