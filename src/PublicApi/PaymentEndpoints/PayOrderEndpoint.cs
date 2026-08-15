using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Route id + body for paying an order.</summary>
public record PayOrderCommand(int OrderId, PayOrderRequest Body);

/// <summary>
/// Shopper action. Authorizes (holds) the order total using either one-off card details or one of
/// the shopper's saved cards. Does not take the money — that happens at fulfilment. Idempotent.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderCommand, IPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public PayOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IPaymentService paymentService) =>
                await HandleAsync(new PayOrderCommand(orderId, request ?? new PayOrderRequest()), paymentService))
            .Produces<PaymentResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderCommand command, IPaymentService paymentService)
    {
        var buyerId = EndpointCaller.RequireBuyerId(_http);

        var card = command.Body.Card is null ? null : PaymentMapping.ToCardDetails(command.Body.Card);
        var payment = await paymentService.AuthorizeOrderAsync(
            command.OrderId, buyerId, card, command.Body.SavedPaymentMethodId);

        return Results.Ok(PaymentMapping.ToPaymentResponse(payment));
    }
}
