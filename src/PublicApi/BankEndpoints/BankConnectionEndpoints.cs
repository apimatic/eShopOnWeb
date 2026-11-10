using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

public sealed record LinkTokenRequest(string? Kind);

public sealed record SaveConnectionRequest(string? PublicToken, string? Kind);

/// <summary>Connect, list and disconnect a shopper's bank and brokerage accounts.</summary>
public class BankConnectionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/link-token",
                [Authorize(AuthenticationSchemes = Shopper.Scheme)] async (LinkTokenRequest? request, ClaimsPrincipal user,
                    BankConnectionService connections) =>
                    Results.Ok(await connections.CreateLinkTokenAsync(Shopper.Id(user), request?.Kind)))
            .WithTags("BankEndpoints");

        app.MapPost("api/bank/connections",
                [Authorize(AuthenticationSchemes = Shopper.Scheme)] async (SaveConnectionRequest request, ClaimsPrincipal user,
                    BankConnectionService connections) =>
                {
                    var connection = await connections.ConnectAsync(Shopper.Id(user), request.PublicToken, request.Kind);
                    return Results.Created($"api/bank/connections/{connection.ConnectionId}", connection);
                })
            .WithTags("BankEndpoints");

        app.MapGet("api/bank/connections",
                [Authorize(AuthenticationSchemes = Shopper.Scheme)] async (ClaimsPrincipal user, BankConnectionService connections) =>
                    Results.Ok(new { connections = await connections.ListAsync(Shopper.Id(user)) }))
            .WithTags("BankEndpoints");

        app.MapDelete("api/bank/connections/{connectionId}",
                [Authorize(AuthenticationSchemes = Shopper.Scheme)] async (string connectionId, ClaimsPrincipal user,
                    BankConnectionService connections) =>
                {
                    await connections.DisconnectAsync(Shopper.Id(user), connectionId);
                    return Results.NoContent();
                })
            .WithTags("BankEndpoints");
    }
}
