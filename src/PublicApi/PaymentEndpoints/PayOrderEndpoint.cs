using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Authorizes (holds) the order total, funded by a one-off card or a saved card.</summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService svc, CancellationToken ct) =>
                await PaymentEndpointHelpers.Guarded(user, async buyerId =>
                {
                    var card = request.Card is { } c
                        ? new CardDetails(c.CardholderName, c.Number, c.Expiry, c.SecurityCode, c.BillingCountryCode, c.BillingPostalCode)
                        : null;
                    var instrument = new PaymentInstrument(card, request.SavedPaymentMethodId);
                    var summary = await svc.AuthorizeAsync(buyerId, orderId, instrument, ct);
                    return Results.Ok(summary);
                }))
            .Produces<OrderPaymentSummary>()
            .WithTags("PaymentEndpoints");
    }
}
