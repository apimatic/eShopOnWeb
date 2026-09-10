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
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total against a one-off card or one of
/// the shopper's saved cards. Does not capture. Idempotent in effect: a repeat returns the existing hold.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId, PayApiRequest request, IPaymentService service, ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var card = request.Card is null
                    ? null
                    : new CardInput(request.Card.Number, request.Card.Expiry, request.Card.SecurityCode,
                        request.Card.Name, request.Card.CountryCode, request.Card.AddressLine1,
                        request.Card.AddressLine2, request.Card.AdminArea1, request.Card.AdminArea2,
                        request.Card.PostalCode);

                var view = await service.AuthorizeAsync(orderId, user.BuyerId(),
                    new PayInput(card, request.SavedCardId), ct);
                return Results.Ok(view);
            })
            .Produces<PaymentView>()
            .WithTags("OrderPaymentEndpoints");
    }
}
