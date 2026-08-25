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
/// Authorizes (holds) the order total with PayPal, using either one-off card details or a saved card.
/// Idempotent: retrying a request that already succeeded replays the current payment state.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderCheckoutService orderCheckoutService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, orderCheckoutService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderCheckoutService orderCheckoutService)
    {
        if (request.Card is null && request.PaymentMethodId is null)
            return Results.BadRequest(new { message = "Supply either card details or a paymentMethodId." });

        if (request.Card is not null && request.PaymentMethodId is not null)
            return Results.BadRequest(new { message = "Supply either card details or a paymentMethodId, not both." });

        var response = new PayOrderResponse(request.CorrelationId());

        var order = await orderCheckoutService.PayAsync(request.BuyerId, request.OrderId,
            request.Card?.ToCardDetails(), request.PaymentMethodId);

        response.OrderId = order.Id;
        response.Order = OrderMapping.ToDto(order);
        return Results.Ok(response);
    }
}
