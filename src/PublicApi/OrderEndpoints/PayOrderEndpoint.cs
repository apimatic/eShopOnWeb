using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes the order total (a hold on the money, not a
/// capture). Funds come from one-off card details, or one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PayOrderRequest request, IPaymentService service, HttpContext ctx) =>
                await HandleAsync(request, service, ctx))
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService service, HttpContext ctx)
    {
        var buyerId = PaymentMapper.GetBuyerId(ctx.User);
        var orderId = PaymentMapper.GetRouteInt(ctx, "orderId");

        PayPalCardDetails? card = request?.Card?.ToCardDetails();
        var order = await service.PayOrderAsync(buyerId, orderId, card, request?.SavedPaymentMethodId, ctx.RequestAborted);

        return Results.Ok(PaymentMapper.ToOrderDto(order));
    }
}

public class PayOrderRequest
{
    /// <summary>One-off card details. Provide this or <see cref="SavedPaymentMethodId"/>.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>A saved card of the caller's to pay with instead of raw card details.</summary>
    public int? SavedPaymentMethodId { get; set; }
}
