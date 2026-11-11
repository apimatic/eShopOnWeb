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

public class ListBanksRequest : BaseRequest
{
    public string? Query { get; init; }
    public int? Count { get; init; }
    public int? Offset { get; init; }
}

/// <summary>
/// The bank picker of checkout: the banks Plaid supports, searched by name.
/// </summary>
public class ListBanksEndpoint : IEndpoint<IResult, ListBanksRequest, BankDirectoryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/banks",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? query, int? count, int? offset, BankDirectoryService directory) =>
            {
                return await HandleAsync(new ListBanksRequest { Query = query, Count = count, Offset = offset }, directory);
            })
            .Produces<BankListDto>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(ListBanksRequest request, BankDirectoryService directory)
    {
        return Results.Ok(await directory.GetBanksAsync(request.Query, request.Count, request.Offset));
    }
}

public class LinkTokenBody
{
    /// <summary>"bank" (the default) or "brokerage".</summary>
    public string? Kind { get; init; }
}

public class CreateLinkTokenRequest : BaseRequest
{
    public ClaimsPrincipal User { get; init; } = null!;
    public string? Kind { get; init; }
}

/// <summary>
/// Creates what the browser needs to open Plaid Link.
/// </summary>
public class CreateLinkTokenEndpoint : IEndpoint<IResult, CreateLinkTokenRequest, BankConnectionService, ShopperResolver>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/link-token",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, LinkTokenBody? body, BankConnectionService connections, ShopperResolver shoppers) =>
            {
                return await HandleAsync(new CreateLinkTokenRequest { User = user, Kind = body?.Kind }, connections, shoppers);
            })
            .Produces<LinkTokenDto>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateLinkTokenRequest request, BankConnectionService connections, ShopperResolver shoppers)
    {
        var shopper = await shoppers.RequireAsync(request.User);
        return Results.Ok(await connections.CreateLinkTokenAsync(shopper, request.Kind));
    }
}

public class ConnectBankBody
{
    public string? PublicToken { get; init; }
    /// <summary>"bank" (the default) or "brokerage".</summary>
    public string? Kind { get; init; }
    /// <summary>The account the shopper picked in Link, if any.</summary>
    public string? AccountId { get; init; }
}

public class ConnectBankRequest : BaseRequest
{
    public ClaimsPrincipal User { get; init; } = null!;
    public ConnectBankBody? Body { get; init; }
}

/// <summary>
/// Saves a connection from what Plaid Link handed the browser.
/// </summary>
public class ConnectBankEndpoint : IEndpoint<IResult, ConnectBankRequest, BankConnectionService, ShopperResolver>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/connections",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ConnectBankBody? body, BankConnectionService connections, ShopperResolver shoppers) =>
            {
                return await HandleAsync(new ConnectBankRequest { User = user, Body = body }, connections, shoppers);
            })
            .Produces<ConnectionDto>(StatusCodes.Status201Created)
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(ConnectBankRequest request, BankConnectionService connections, ShopperResolver shoppers)
    {
        var shopper = await shoppers.RequireAsync(request.User);
        var connection = await connections.ConnectAsync(shopper, request.Body?.PublicToken, request.Body?.Kind, request.Body?.AccountId);
        return Results.Created($"api/bank/connections/{connection.Id}", connection);
    }
}

public class ListBankConnectionsRequest : BaseRequest
{
    public ClaimsPrincipal User { get; init; } = null!;
}

/// <summary>
/// Lists the connections of the shopper (never their secrets).
/// </summary>
public class ListBankConnectionsEndpoint : IEndpoint<IResult, ListBankConnectionsRequest, BankConnectionService, ShopperResolver>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/bank/connections",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, BankConnectionService connections, ShopperResolver shoppers) =>
            {
                return await HandleAsync(new ListBankConnectionsRequest { User = user }, connections, shoppers);
            })
            .Produces<System.Collections.Generic.IReadOnlyList<ConnectionDto>>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(ListBankConnectionsRequest request, BankConnectionService connections, ShopperResolver shoppers)
    {
        var shopper = await shoppers.RequireAsync(request.User);
        return Results.Ok(await connections.ListAsync(shopper));
    }
}

public class DisconnectBankRequest : BaseRequest
{
    public ClaimsPrincipal User { get; init; } = null!;
    public int ConnectionId { get; init; }
}

/// <summary>
/// Disconnects a bank or brokerage account: Plaid stops billing for the Item and the shop forgets it.
/// </summary>
public class DisconnectBankEndpoint : IEndpoint<IResult, DisconnectBankRequest, BankConnectionService, ShopperResolver>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/bank/connections/{connectionId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, int connectionId, BankConnectionService connections, ShopperResolver shoppers) =>
            {
                return await HandleAsync(new DisconnectBankRequest { User = user, ConnectionId = connectionId }, connections, shoppers);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(DisconnectBankRequest request, BankConnectionService connections, ShopperResolver shoppers)
    {
        var shopper = await shoppers.RequireAsync(request.User);
        await connections.DisconnectAsync(shopper, request.ConnectionId);
        return Results.NoContent();
    }
}
