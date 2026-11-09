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

namespace Microsoft.eShopWeb.PublicApi.AccountEndpoints;

/// <summary>
/// Closes the signed-in shopper's account and erases their data, at Stax and in the shop
/// </summary>
public class CloseAccountEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ShopperService shoppers, AccountService accounts, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(user);
                await accounts.CloseAsync(email, ct);
                return Results.Ok(new { closed = true });
            })
            .WithTags("AccountEndpoints");
    }
}
