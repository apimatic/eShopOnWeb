using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Card details for a one-off payment, OR a saved card id — supply one. Full card details are never
/// stored or logged; they pass straight to PayPal.
/// </summary>
public class PayOrderRequest
{
    public string? CardNumber { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public int? SavedCardId { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total. Shopper-scoped, idempotent.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var instruction = new PaymentInstruction
                {
                    CardNumber = request.CardNumber,
                    Expiry = request.Expiry,
                    SecurityCode = request.SecurityCode,
                    CardholderName = request.CardholderName,
                    SavedCardId = request.SavedCardId
                };

                var payment = await paymentService.PayAsync(orderId, buyerId, instruction, ct);
                return Results.Ok(PaymentView.From(payment));
            })
            .Produces<PaymentView>()
            .WithTags("OrderPaymentEndpoints");
    }
}
