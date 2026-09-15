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
/// Authorizes (holds) the order total. The money is held, not taken — capture happens at fulfilment.
/// Pay with raw card details or one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext http, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                request.BuyerId = http.User.Identity?.Name;
                return await HandleAsync(request, service);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var instruction = new PayInstruction(request.Card?.ToCardDetails(), request.PaymentMethodId);
        var payment = await service.AuthorizeAsync(request.OrderId, request.BuyerId, instruction);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            Payment = PaymentStateDto.From(payment)
        };

        return Results.Ok(response);
    }
}
