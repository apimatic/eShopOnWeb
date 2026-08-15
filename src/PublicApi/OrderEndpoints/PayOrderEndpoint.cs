using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Body for paying an order: either raw card details or a saved card id (exactly one).</summary>
public class PayOrderRequest
{
    public CardInput? Card { get; set; }
    public int? SavedCardId { get; set; }
}

/// <summary>
/// Authorizes (holds) the order total with PayPal. Does not capture. Shopper-scoped: acts only on
/// the caller's own order. Idempotent — a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Authorize (hold) payment for an order", Tags = new[] { "OrderPaymentEndpoints" })]
            async (int orderId, PayOrderRequest request, IPaymentService paymentService, IPaymentConfiguration config,
                   HttpContext http, CancellationToken ct) =>
            {
                var buyerId = http.User.GetBuyerId();
                var instruction = new PaymentInstruction
                {
                    Card = request.Card?.ToCardDetails(),
                    SavedCardId = request.SavedCardId
                };

                var state = await paymentService.AuthorizeAsync(orderId, buyerId, instruction, ct);
                return Results.Ok(PaymentResponseFactory.From(state, config.Currency));
            })
            .Produces<OrderPaymentView>()
            .WithTags("OrderPaymentEndpoints");
    }
}
