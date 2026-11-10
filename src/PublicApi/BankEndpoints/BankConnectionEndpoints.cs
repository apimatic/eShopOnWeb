using System;
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

public class LinkTokenRequest : BaseRequest
{
    public string ShopperName { get; set; } = "";

    /// <summary>"bank" (the default) or "brokerage".</summary>
    public string? Type { get; set; }
}

public class LinkTokenResponse : BaseResponse
{
    public LinkTokenResponse(Guid correlationId) : base(correlationId)
    {
    }

    public LinkTokenResponse()
    {
    }

    public string LinkToken { get; set; } = "";
    public DateTimeOffset Expiration { get; set; }
}

/// <summary>
/// Gives the browser what it needs to open Plaid Link
/// </summary>
public class CreateLinkTokenEndpoint : IEndpoint<IResult, LinkTokenRequest, BankConnectionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/link-token",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (LinkTokenRequest? request, ClaimsPrincipal user, BankConnectionService connections) =>
            {
                var shopper = BankEndpointSupport.ShopperOf(user);
                return await HandleAsync(new LinkTokenRequest { ShopperName = shopper, Type = request?.Type }, connections);
            })
            .Produces<LinkTokenResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(LinkTokenRequest request, BankConnectionService connections)
    {
        var token = await connections.CreateLinkTokenAsync(request.ShopperName, request.Type);
        return Results.Ok(new LinkTokenResponse(request.CorrelationId()) { LinkToken = token.LinkToken, Expiration = token.Expiration });
    }
}

public class CreateBankConnectionRequest : BaseRequest
{
    public string ShopperName { get; set; } = "";

    /// <summary>The public token Plaid Link handed the browser.</summary>
    public string? PublicToken { get; set; }

    /// <summary>"bank" (the default) or "brokerage".</summary>
    public string? Type { get; set; }

    /// <summary>The account chosen in Link, when the connection has several.</summary>
    public string? AccountId { get; set; }
}

public class CreateBankConnectionResponse : BaseResponse
{
    public CreateBankConnectionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateBankConnectionResponse()
    {
    }

    public BankConnectionDto? Connection { get; set; }
}

/// <summary>
/// Saves a bank or brokerage connection from what Plaid Link handed the browser
/// </summary>
public class CreateBankConnectionEndpoint : IEndpoint<IResult, CreateBankConnectionRequest, BankConnectionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/connections",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateBankConnectionRequest request, ClaimsPrincipal user, BankConnectionService connections) =>
            {
                request.ShopperName = BankEndpointSupport.ShopperOf(user);
                return await HandleAsync(request, connections);
            })
            .Produces<CreateBankConnectionResponse>(StatusCodes.Status201Created)
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateBankConnectionRequest request, BankConnectionService connections)
    {
        var connection = await connections.ConnectAsync(request.ShopperName, request.PublicToken, request.Type, request.AccountId);
        return Results.Created($"api/bank/connections/{connection.Id}",
            new CreateBankConnectionResponse(request.CorrelationId()) { Connection = connection });
    }
}

public class DeleteBankConnectionRequest : BaseRequest
{
    public string ShopperName { get; init; } = "";
    public int ConnectionId { get; init; }
}

/// <summary>
/// Disconnects a bank or brokerage account; the shop stops paying Plaid for it
/// </summary>
public class DeleteBankConnectionEndpoint : IEndpoint<IResult, DeleteBankConnectionRequest, BankConnectionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/bank/connections/{connectionId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int connectionId, ClaimsPrincipal user, BankConnectionService connections) =>
            {
                return await HandleAsync(new DeleteBankConnectionRequest
                {
                    ShopperName = BankEndpointSupport.ShopperOf(user),
                    ConnectionId = connectionId,
                }, connections);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteBankConnectionRequest request, BankConnectionService connections)
    {
        await connections.DisconnectAsync(request.ShopperName, request.ConnectionId);
        return Results.NoContent();
    }
}
