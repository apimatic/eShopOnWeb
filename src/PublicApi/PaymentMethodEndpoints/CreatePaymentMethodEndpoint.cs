using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Stax;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card or bank account for the signed-in shopper
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (JsonObject body, ClaimsPrincipal user, ShopperService shoppers, PaymentMethodService methods, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(user);
                var saved = await methods.SaveAsync(email, body, ct);
                return Results.Created($"api/payment-methods/{saved.Id}", saved);
            })
            .WithTags("PaymentMethodEndpoints");
    }
}
