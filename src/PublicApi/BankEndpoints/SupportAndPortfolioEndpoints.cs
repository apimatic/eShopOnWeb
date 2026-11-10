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

public class BankActivityRequest : BaseRequest
{
    public string Email { get; init; } = "";
    public string? From { get; init; }
    public string? To { get; init; }
}

public class BankActivityResponse : BaseResponse
{
    public BankActivityResponse(Guid correlationId) : base(correlationId)
    {
    }

    public BankActivityResponse()
    {
    }

    public List<BankTransactionDto> Transactions { get; set; } = new();

    /// <summary>Banks that could not be read right now, and why; the others are still listed.</summary>
    public List<UnavailableBank> Unavailable { get; set; } = new();
}

/// <summary>
/// Support: the activity on all the banks a shopper connected, from the shop's own up-to-date copy
/// </summary>
public class BankActivityEndpoint : IEndpoint<IResult, BankActivityRequest, BankActivityService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/bank-activity",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, BankActivityService activity) =>
            {
                return await HandleAsync(new BankActivityRequest { Email = email }, activity);
            })
            .Produces<BankActivityResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(BankActivityRequest request, BankActivityService activity)
    {
        var result = await activity.GetActivityAsync(request.Email);
        return Results.Ok(new BankActivityResponse(request.CorrelationId())
        {
            Transactions = result.Transactions,
            Unavailable = result.Unavailable,
        });
    }
}

/// <summary>
/// Support: the transactions between two dates on the banks a shopper connected, straight from the bank
/// </summary>
public class BankTransactionsEndpoint : IEndpoint<IResult, BankActivityRequest, BankActivityService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/transactions",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, string? from, string? to, BankActivityService activity) =>
            {
                return await HandleAsync(new BankActivityRequest { Email = email, From = from, To = to }, activity);
            })
            .Produces<BankActivityResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(BankActivityRequest request, BankActivityService activity)
    {
        var result = await activity.GetTransactionsAsync(request.Email, request.From, request.To);
        return Results.Ok(new BankActivityResponse(request.CorrelationId())
        {
            Transactions = result.Transactions,
            Unavailable = result.Unavailable,
        });
    }
}

public class PortfolioRequest : BaseRequest
{
    public string ShopperName { get; init; } = "";
}

public class PortfolioResponse : BaseResponse
{
    public PortfolioResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PortfolioResponse()
    {
    }

    public List<PositionDto> Positions { get; set; } = new();
    public List<UnavailableBank> Unavailable { get; set; } = new();
}

/// <summary>
/// The holdings of the shopper's connected brokerage accounts
/// </summary>
public class PortfolioEndpoint : IEndpoint<IResult, PortfolioRequest, PortfolioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/portfolio",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, PortfolioService portfolio) =>
            {
                return await HandleAsync(new PortfolioRequest { ShopperName = BankEndpointSupport.ShopperOf(user) }, portfolio);
            })
            .Produces<PortfolioResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(PortfolioRequest request, PortfolioService portfolio)
    {
        var result = await portfolio.GetAsync(request.ShopperName);
        return Results.Ok(new PortfolioResponse(request.CorrelationId())
        {
            Positions = result.Positions,
            Unavailable = result.Unavailable,
        });
    }
}
