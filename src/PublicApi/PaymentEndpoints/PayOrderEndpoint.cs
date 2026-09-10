using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest
{
    public int OrderId { get; set; }

    /// <summary>Raw card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>The id of one of the shopper's saved cards to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class PayOrderResponse
{
    public OrderDto? Order { get; set; }
}

/// <summary>
/// Authorizes (holds) the order total on the shopper's card — either raw card details or a saved card.
/// The money is not taken here; that happens at fulfilment. Shopper-scoped and idempotent.
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
            .Produces<PayOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service)
    {
        var buyerId = user.GetBuyerId();
        var input = new PayOrderInput(request.Card?.ToDomain(), request.SavedPaymentMethodId);
        var order = await service.AuthorizeAsync(buyerId, request.OrderId, input);
        return Results.Ok(new PayOrderResponse { Order = OrderDto.From(order) });
    }
}
