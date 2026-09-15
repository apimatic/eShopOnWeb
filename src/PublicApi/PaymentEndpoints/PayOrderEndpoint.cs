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

/// <summary>
/// Authorizes (holds) the order total against a card. The request carries either raw card details for a one-off
/// payment or the id of one of the shopper's saved cards. Idempotent: a double-click does not authorize twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest>
{
    private readonly IPaymentService _payments;

    public PayOrderEndpoint(IPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request);
            })
            .Produces<PaymentActionResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request)
    {
        var instruction = new PayInstruction(request.Card?.ToCardDetails(), request.SavedPaymentMethodId);
        var payment = await _payments.AuthorizeAsync(request.OrderId, request.BuyerId, instruction);
        return Results.Ok(new PaymentActionResponse(payment.ToDto()));
    }
}
