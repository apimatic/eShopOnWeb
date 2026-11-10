using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Plaid;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

internal static class CallerExtensions
{
    /// <summary>The caller's identity, taken from the JWT.</summary>
    public static string Buyer(this ClaimsPrincipal user) =>
        user.Identity?.Name is { Length: > 0 } name ? name : throw new ApiProblemException(401, "The token does not identify a user.");
}

public class BankListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/banks", [AllowAnonymous] async (string? query, int? count, int? offset, BankDirectory directory, CancellationToken ct) =>
            Results.Ok(await directory.GetBanksAsync(query, count, offset, ct)))
            .Produces(200)
            .WithTags("BankEndpoints");
    }

}

public class LinkTokenRequest
{
    /// <summary>"bank" (default) or "brokerage".</summary>
    public string? Type { get; set; }
    public string? InstitutionId { get; set; }
    /// <summary>Set to re-open Link for a connection that exists already.</summary>
    public int? ConnectionId { get; set; }
}

public class CreateLinkTokenEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/link-token",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (LinkTokenRequest? request, ClaimsPrincipal user, BankConnectionService service, CancellationToken ct) =>
            {
                var (token, expiration) = await service.CreateLinkTokenAsync(
                    user.Buyer(), request?.Type, request?.InstitutionId, request?.ConnectionId, ct);
                return Results.Ok(new { linkToken = token, expiration });
            })
            .Produces(200)
            .WithTags("BankEndpoints");
    }
}

public class ConnectBankRequest
{
    public string? PublicToken { get; set; }
    /// <summary>"bank" (default) or "brokerage".</summary>
    public string? Type { get; set; }
    public string? AccountId { get; set; }
}

public class CreateBankConnectionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/connections",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ConnectBankRequest? request, ClaimsPrincipal user, BankConnectionService service, CancellationToken ct) =>
            {
                var connection = await service.ConnectAsync(user.Buyer(), request?.PublicToken, request?.Type, request?.AccountId, ct);
                return Results.Created($"api/bank/connections/{connection.ConnectionId}", connection);
            })
            .Produces<ConnectionDto>(201)
            .WithTags("BankEndpoints");
    }
}

public class ListBankConnectionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/bank/connections",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, BankConnectionService service, CancellationToken ct) =>
                Results.Ok(new { connections = await service.ListAsync(user.Buyer(), ct) }))
            .Produces(200)
            .WithTags("BankEndpoints");
    }
}

public class DeleteBankConnectionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/bank/connections/{connectionId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int connectionId, ClaimsPrincipal user, BankConnectionService service, CancellationToken ct) =>
            {
                await service.DisconnectAsync(user.Buyer(), connectionId, ct);
                return Results.NoContent();
            })
            .Produces(204)
            .WithTags("BankEndpoints");
    }
}
