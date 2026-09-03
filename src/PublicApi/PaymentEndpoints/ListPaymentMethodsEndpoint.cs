using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>GET /api/payment-methods — the caller's saved cards (safe descriptions only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ISavedCardService service,
                HttpContext http,
                System.Threading.CancellationToken ct) =>
            {
                var buyerId = http.User.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var cards = await service.GetCardsAsync(buyerId, ct);
                return Results.Ok(cards.Select(PaymentMethodResponse.From).ToList());
            })
            .Produces<List<PaymentMethodResponse>>()
            .WithTags("PaymentEndpoints");
    }
}
