using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.PayByBank;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PayByBankEndpoints;

public record ListBanksRequest(string? Query, int? Count, int? Offset);

/// <summary>Lists the banks Plaid supports, optionally searched by name (the checkout bank picker).</summary>
public class ListBanksEndpoint : IEndpoint<IResult, ListBanksRequest, BankDirectoryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/banks",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? query, int? count, int? offset, BankDirectoryService banks) =>
            {
                return await HandleAsync(new ListBanksRequest(query, count, offset), banks);
            })
            .Produces<BankListDto>()
            .WithTags("PayByBankEndpoints");
    }

    public async Task<IResult> HandleAsync(ListBanksRequest request, BankDirectoryService banks) =>
        Results.Ok(await banks.ListAsync(request.Query, request.Count, request.Offset));
}

public record CreateLinkTokenBody(string? Kind, string? InstitutionId);

public record CreateLinkTokenRequest(string UserId, CreateLinkTokenBody? Body);

/// <summary>Gives the browser what it needs to open Plaid Link for the signed-in shopper.</summary>
public class CreateLinkTokenEndpoint : IEndpoint<IResult, CreateLinkTokenRequest, BankConnectionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/link-token",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, BankConnectionService connections, CreateLinkTokenBody? body) =>
            {
                return await HandleAsync(new CreateLinkTokenRequest(Caller.Id(user), body), connections);
            })
            .Produces<LinkTokenDto>()
            .WithTags("PayByBankEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateLinkTokenRequest request, BankConnectionService connections) =>
        Results.Ok(await connections.CreateLinkTokenAsync(request.UserId, request.Body?.Kind, request.Body?.InstitutionId));
}

public record ConnectBankBody(string? PublicToken, string? Kind, string? AccountId);

public record ConnectBankRequest(string UserId, ConnectBankBody? Body);

/// <summary>Saves the connection Plaid Link handed the browser, so it can be used on later visits.</summary>
public class ConnectBankEndpoint : IEndpoint<IResult, ConnectBankRequest, BankConnectionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/connections",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, BankConnectionService connections, ConnectBankBody? body) =>
            {
                return await HandleAsync(new ConnectBankRequest(Caller.Id(user), body), connections);
            })
            .Produces<ConnectionDto>(StatusCodes.Status201Created)
            .WithTags("PayByBankEndpoints");
    }

    public async Task<IResult> HandleAsync(ConnectBankRequest request, BankConnectionService connections)
    {
        var connection = await connections.ConnectAsync(request.UserId, request.Body?.PublicToken, request.Body?.Kind, request.Body?.AccountId);
        return Results.Created($"api/bank/connections/{connection.ConnectionId}", connection);
    }
}

public record DisconnectBankRequest(string UserId, int ConnectionId);

/// <summary>Disconnects a bank or brokerage account; Plaid stops billing once its Item is removed.</summary>
public class DisconnectBankEndpoint : IEndpoint<IResult, DisconnectBankRequest, BankConnectionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/bank/connections/{connectionId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int connectionId, ClaimsPrincipal user, BankConnectionService connections) =>
            {
                return await HandleAsync(new DisconnectBankRequest(Caller.Id(user), connectionId), connections);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PayByBankEndpoints");
    }

    public async Task<IResult> HandleAsync(DisconnectBankRequest request, BankConnectionService connections)
    {
        await connections.DisconnectAsync(request.UserId, request.ConnectionId);
        return Results.NoContent();
    }
}

public record PortfolioRequest(string UserId);

public record PortfolioResponse(System.Collections.Generic.IReadOnlyList<PositionDto> Positions);

/// <summary>The holdings of the shopper's connected brokerage accounts.</summary>
public class GetPortfolioEndpoint : IEndpoint<IResult, PortfolioRequest, PortfolioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/portfolio",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, PortfolioService portfolio) =>
            {
                return await HandleAsync(new PortfolioRequest(Caller.Id(user)), portfolio);
            })
            .Produces<PortfolioResponse>()
            .WithTags("PayByBankEndpoints");
    }

    public async Task<IResult> HandleAsync(PortfolioRequest request, PortfolioService portfolio) =>
        Results.Ok(new PortfolioResponse(await portfolio.GetPositionsAsync(request.UserId)));
}
