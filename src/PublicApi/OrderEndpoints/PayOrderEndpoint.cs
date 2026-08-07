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

/// <summary>
/// Pays for an order with PayPal, using either a one-off card or one of the shopper's saved cards.
/// Idempotent in effect: a double-click never produces a second charge.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, HttpContext>
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
            (int orderId, PayOrderRequest request, HttpContext http) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, http);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, HttpContext http)
    {
        var buyerId = http.User.GetBuyerId();

        var instruction = new PaymentInstruction
        {
            Card = request.Card?.ToCardDetails(),
            SavedPaymentMethodId = request.SavedPaymentMethodId
        };

        var order = await _orderPaymentService.PayOrderAsync(request.OrderId, buyerId, instruction, http.RequestAborted);

        return Results.Ok(new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = order.ToSummary()
        });
    }
}
