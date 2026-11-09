using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.AccountEndpoints;

/// <summary>
/// Closes the signed-in shopper account and erases their data, at Stax and in the shop
/// </summary>
public class CloseAccountEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, AccountService service, CancellationToken ct) =>
            {
                var result = await service.CloseAsync(user.Email(), ct);
                return Results.Ok(new { closed = true });
            })
            .Produces<object>()
            .WithTags("AccountEndpoints");
    }
}
