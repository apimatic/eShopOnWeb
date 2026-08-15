using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>GET /api/payment-methods — the caller's saved cards (shopper-scoped).</summary>
public class ListCardsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        async (ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
            {
                var buyerId = CallerContext.BuyerId(user);
                var cards = await service.ListCardsAsync(buyerId, ct);
                return Results.Ok(new ListCardsResponse { PaymentMethods = cards });
            })
            .Produces<ListCardsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }
}
