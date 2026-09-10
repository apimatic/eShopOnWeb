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

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total. Carries either one-off card
/// details or the id of one of the shopper's saved cards. Does not capture; the hold equals the order
/// total to the cent. Idempotent: a double-click does not authorize twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                PayOrderRequest request,
                ClaimsPrincipal user,
                IPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);

                CardDetails? card = request.Card is { } c
                    ? new CardDetails(c.Number, c.Expiry, c.SecurityCode, c.CardholderName,
                        c.AddressLine1, c.AddressLine2, c.City, c.State, c.PostalCode, c.CountryCode)
                    : null;

                var command = new PayRequestCommand(card, request.SavedPaymentMethodId);
                var payment = await paymentService.PayAsync(buyerId, orderId, command, cancellationToken);

                return Results.Ok(new { orderId, payment = PaymentStateDto.From(payment) });
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("PaymentEndpoints");
    }
}
