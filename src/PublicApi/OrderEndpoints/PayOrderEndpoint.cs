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
/// Authorizes the order total — puts a hold on the shopper's money without taking it. The money is
/// only taken when the order is fulfilled.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IPaymentService paymentService, HttpContext context) =>
            {
                // The route is the authority on which order is being paid for.
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService, context);
            })
            .Produces<PaymentResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService,
        HttpContext context)
    {
        var buyerId = context.BuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var hasCard = request.Card is not null;
        var hasSavedCard = request.PaymentMethodId is not null;

        if (hasCard == hasSavedCard)
        {
            return Results.BadRequest(new
            {
                message = "Send either 'card' with card details, or 'paymentMethodId' naming one of your " +
                          "saved cards — exactly one of the two."
            });
        }

        PaymentInstrument instrument = hasCard
            ? new PaymentInstrument.OneOffCard(request.Card!.ToCardDetails())
            : new PaymentInstrument.SavedCardReference(request.PaymentMethodId!.Value);

        var payment = await paymentService.AuthorizeAsync(buyerId, request.OrderId, instrument,
            context.RequestAborted);

        return Results.Ok(new PaymentResponse(request.CorrelationId()) { Payment = payment });
    }
}
