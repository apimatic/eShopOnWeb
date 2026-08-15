using System.Collections.Generic;
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
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>GET /api/payment-methods — the caller's own saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, ISavedCardService service, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                var cards = await service.ListCardsAsync(buyerId, ct);
                return Results.Ok(cards.Select(PaymentMethodDto.FromEntity).ToList());
            })
            .Produces<List<PaymentMethodDto>>()
            .WithTags("PaymentMethodEndpoints");
    }
}
