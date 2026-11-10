using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

public class LinkTokenRequestBody
{
    /// <summary>"bank" (default) to pay by bank, or "brokerage" for the portfolio page.</summary>
    public string? Purpose { get; init; }
}

public class LinkTokenRequest : BaseRequest
{
    public string BuyerId { get; init; } = string.Empty;
    public string? Purpose { get; init; }
}

public record LinkTokenResponse(string LinkToken, DateTimeOffset Expiration);

/// <summary>
/// Creates what the browser needs to open Plaid Link
/// </summary>
public class CreateLinkTokenEndpoint : IEndpoint<IResult, LinkTokenRequest, BankConnectionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/link-token",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, LinkTokenRequestBody? body, BankConnectionService service) =>
            {
                return await HandleAsync(new LinkTokenRequest { BuyerId = BankEndpointSupport.Caller(user), Purpose = body?.Purpose }, service);
            })
            .Produces<LinkTokenResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(LinkTokenRequest request, BankConnectionService service)
    {
        var token = await service.CreateLinkTokenAsync(request.BuyerId, request.Purpose, default);
        return Results.Ok(new LinkTokenResponse(token.LinkToken, token.Expiration));
    }
}

public class SaveConnectionRequestBody
{
    /// <summary>The public token Plaid Link hands the browser.</summary>
    public string? PublicToken { get; init; }

    /// <summary>The account the shopper picked in Link (optional).</summary>
    public string? AccountId { get; init; }

    /// <summary>"bank" or "brokerage" (optional; inferred from the connection when omitted).</summary>
    public string? Purpose { get; init; }
}

public class SaveConnectionRequest : BaseRequest
{
    public string BuyerId { get; init; } = string.Empty;
    public SaveConnectionRequestBody Body { get; init; } = new();
}

public record ConnectionResponse(
    int Id,
    int ConnectionId,
    string Kind,
    string? InstitutionId,
    string? InstitutionName,
    string? AccountName,
    string? AccountMask,
    DateTimeOffset CreatedAt)
{
    public static ConnectionResponse From(BankConnectionInfo c) =>
        new(c.Id, c.Id, c.Kind, c.InstitutionId, c.InstitutionName, c.AccountName, c.AccountMask, c.CreatedAt);
}

/// <summary>
/// Saves a bank or brokerage connection from what Plaid Link handed the browser
/// </summary>
public class SaveConnectionEndpoint : IEndpoint<IResult, SaveConnectionRequest, BankConnectionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/connections",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, SaveConnectionRequestBody body, BankConnectionService service) =>
            {
                return await HandleAsync(new SaveConnectionRequest { BuyerId = BankEndpointSupport.Caller(user), Body = body }, service);
            })
            .Produces<ConnectionResponse>(StatusCodes.Status201Created)
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(SaveConnectionRequest request, BankConnectionService service)
    {
        var connection = await service.AddConnectionAsync(
            request.BuyerId, request.Body.PublicToken ?? string.Empty, request.Body.AccountId, request.Body.Purpose, default);
        return Results.Created($"api/bank/connections/{connection.Id}", ConnectionResponse.From(connection));
    }
}

public class ListConnectionsRequest : BaseRequest
{
    public string BuyerId { get; init; } = string.Empty;
}

public record ListConnectionsResponse(IReadOnlyList<ConnectionResponse> Connections);

/// <summary>
/// Lists the caller's connected banks and brokerage accounts
/// </summary>
public class ListConnectionsEndpoint : IEndpoint<IResult, ListConnectionsRequest, BankConnectionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/bank/connections",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, BankConnectionService service) =>
            {
                return await HandleAsync(new ListConnectionsRequest { BuyerId = BankEndpointSupport.Caller(user) }, service);
            })
            .Produces<ListConnectionsResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(ListConnectionsRequest request, BankConnectionService service)
    {
        var items = await service.ListAsync(request.BuyerId, default);
        return Results.Ok(new ListConnectionsResponse(items.Select(ConnectionResponse.From).ToList()));
    }
}

public class DisconnectRequest : BaseRequest
{
    public string BuyerId { get; init; } = string.Empty;
    public int ConnectionId { get; init; }
}

public record DisconnectResponse(int ConnectionId, bool Removed);

/// <summary>
/// Disconnects a bank or brokerage account; Plaid stops billing the shop for it
/// </summary>
public class DisconnectEndpoint : IEndpoint<IResult, DisconnectRequest, BankConnectionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/bank/connections/{connectionId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, int connectionId, BankConnectionService service) =>
            {
                return await HandleAsync(new DisconnectRequest { BuyerId = BankEndpointSupport.Caller(user), ConnectionId = connectionId }, service);
            })
            .Produces<DisconnectResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(DisconnectRequest request, BankConnectionService service)
    {
        await service.RemoveAsync(request.BuyerId, request.ConnectionId, default);
        return Results.Ok(new DisconnectResponse(request.ConnectionId, true));
    }
}
