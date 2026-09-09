using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Instrument for a pay request: one-off card details, or a saved card id.</summary>
public class PayOrderRequest : BaseRequest
{
    public CardModel? Card { get; set; }
    public int? SavedCardId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public PaymentStateModel? Payment { get; set; }
}

/// <summary>
/// Authorizes (holds) the order total for the caller's own order. The hold equals the order
/// total to the cent; the money is not taken until fulfilment. Idempotent in effect.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);
                var instrument = new PaymentInstrument(request.Card?.ToPayPalCardInput(), request.SavedCardId);
                var payment = await paymentService.AuthorizeAsync(buyerId, orderId, instrument, ct);

                return Results.Ok(new PayOrderResponse(request.CorrelationId())
                {
                    OrderId = orderId,
                    Payment = PaymentStateModel.From(payment)
                });
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
