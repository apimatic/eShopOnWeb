using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

/// <summary>
/// Disconnects a bank or brokerage account
/// </summary>
public class DisconnectEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/bank/connections/{connectionId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int connectionId, ClaimsPrincipal user, ConnectionService connections, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(connectionId, user, connections, cancellationToken);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(int connectionId, ClaimsPrincipal user, ConnectionService connections,
        CancellationToken cancellationToken)
    {
        await connections.DisconnectAsync(user.ShopperId(), connectionId, cancellationToken);
        return Results.NoContent();
    }
}
