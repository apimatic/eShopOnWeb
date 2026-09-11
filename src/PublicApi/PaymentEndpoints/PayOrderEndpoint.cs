using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record PayOrderCommand(int OrderId, PayOrderApiRequest Body);

/// <summary>
/// Authorizes (holds) the order total using one-off card details or one of the shopper's saved
/// cards. Idempotent in effect: a double request never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderCommand, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderApiRequest body, IPaymentService paymentService) =>
                await HandleAsync(new PayOrderCommand(orderId, body ?? new PayOrderApiRequest()), paymentService))
            .Produces<PaymentStateResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(PayOrderCommand request, IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.BuyerId();

        var payRequest = new PayOrderRequest(
            request.Body.Card?.ToCardDetails(),
            request.Body.SavedPaymentMethodId);

        var payment = await paymentService.AuthorizeAsync(buyerId!, request.OrderId, payRequest);
        return Results.Ok(PaymentStateResponse.FromPayment(payment));
    }
}
