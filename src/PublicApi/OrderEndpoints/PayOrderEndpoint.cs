using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes an order: the order total is put on hold, not taken. Paid either with a one-off card or
/// with one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal caller, IPaymentProcessingService payments) =>
            {
                request.OrderId = orderId;
                request.Actor = RequestActor.From(caller);
                return await HandleAsync(request, payments);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentProcessingService payments)
    {
        var actor = request.RequireActor();
        var response = new PayOrderResponse(request.CorrelationId()) { OrderId = request.OrderId };

        if (request.Card is null && request.PaymentMethodId is null)
        {
            return Results.BadRequest(new
            {
                message = "Send either card details or a paymentMethodId from api/payment-methods."
            });
        }

        var result = await payments.PayAsync(actor.BuyerId, request.OrderId,
            actor.ToCardDetails(request.Card), request.PaymentMethodId);

        response.OrderStatus = result.Order.Status.ToString();
        response.AlreadyRecorded = result.AlreadyRecorded;
        response.Payment = result.Payment is null ? null : PaymentDto.From(result.Payment);
        response.Note = result.Note;

        return Results.Ok(response);
    }
}
