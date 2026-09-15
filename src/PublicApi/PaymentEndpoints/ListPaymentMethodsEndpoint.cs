using System.Linq;
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
/// GET /api/payment-methods — the caller's saved cards (safe descriptors only).
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IPaymentMethodService paymentMethodService,
                CancellationToken cancellationToken) =>
            await PaymentProblem.ExecuteAsync(async () =>
            {
                var buyerId = user.GetBuyerId();
                var cards = await paymentMethodService.ListCardsAsync(buyerId, cancellationToken);
                return Results.Ok(cards.Select(SavedCardDto.From).ToList());
            }))
            .Produces<System.Collections.Generic.List<SavedCardDto>>()
            .WithTags("PaymentMethodEndpoints");
    }
}
