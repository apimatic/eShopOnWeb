using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Authorizes (holds) the order total using either a one-off card or a saved payment method.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
                {
                    request.OrderId = orderId;
                    return await HandleAsync(request, user, paymentService);
                })
            .Produces<PayOrderResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user,
        IPaymentService paymentService)
    {
        var card = request.ResolveCard();
        var paymentMethodId = request.PaymentMethodId ?? request.SavedPaymentMethodId;

        if (card is null && !paymentMethodId.HasValue)
        {
            throw PaymentException.BadRequest(
                "Provide either card details or paymentMethodId for a saved card.");
        }

        var payment = await paymentService.PayOrderAsync(user.GetBuyerId(), request.OrderId,
            new PayOrderCommand(paymentMethodId, card));

        return Results.Ok(new PayOrderResponse
        {
            OrderId = request.OrderId,
            Payment = PaymentMapping.ToPaymentResponse(payment)
        });
    }
}
