using System;
using System.Globalization;
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
    public string Email { get; init; } = string.Empty;
}

/// <summary>
/// For support staff: the activity on every bank a shopper connected, from the shop's own copy,
/// first brought in step with the bank's records.
/// </summary>
public class BankActivityEndpoint : IEndpoint<IResult, BankActivityRequest, BankActivityService, ShopperResolver>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/bank-activity",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, BankActivityService activity, ShopperResolver shoppers) =>
            {
                return await HandleAsync(new BankActivityRequest { Email = email }, activity, shoppers);
            })
            .Produces<BankActivityDto>()
            .WithTags("SupportEndpoints");
    }

    public async Task<IResult> HandleAsync(BankActivityRequest request, BankActivityService activity, ShopperResolver shoppers)
    {
        var shopper = await shoppers.FindByEmailAsync(request.Email);
        return Results.Ok(await activity.GetActivityAsync(shopper.Id, request.Email));
    }
}

public class BankTransactionsRequest : BaseRequest
{
    public string Email { get; init; } = string.Empty;
    public string? From { get; init; }
    public string? To { get; init; }
}

/// <summary>
/// For support staff: the transactions between two dates on the shopper's connected banks, straight from the bank.
/// </summary>
public class BankTransactionsEndpoint : IEndpoint<IResult, BankTransactionsRequest, BankActivityService, ShopperResolver>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/transactions",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, string? from, string? to, BankActivityService activity, ShopperResolver shoppers) =>
            {
                return await HandleAsync(new BankTransactionsRequest { Email = email, From = from, To = to }, activity, shoppers);
            })
            .Produces<DatedTransactionsDto>()
            .WithTags("SupportEndpoints");
    }

    private static DateOnly ParseDate(string? value, string name)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new BankingException(400, $"{name} is required, as YYYY-MM-DD.");
        return date;
    }

    public async Task<IResult> HandleAsync(BankTransactionsRequest request, BankActivityService activity, ShopperResolver shoppers)
    {
        var from = ParseDate(request.From, "from");
        var to = ParseDate(request.To, "to");
        if (from > to) throw new BankingException(400, "from must not be after to.");
        var shopper = await shoppers.FindByEmailAsync(request.Email);
        return Results.Ok(await activity.GetBetweenAsync(shopper.Id, request.Email, from, to));
    }
}

public class PortfolioRequest : BaseRequest
{
    public ClaimsPrincipal User { get; init; } = null!;
}

/// <summary>
/// The holdings of the shopper's connected brokerage accounts.
/// </summary>
public class PortfolioEndpoint : IEndpoint<IResult, PortfolioRequest, PortfolioService, ShopperResolver>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/portfolio",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, PortfolioService portfolio, ShopperResolver shoppers) =>
            {
                return await HandleAsync(new PortfolioRequest { User = user }, portfolio, shoppers);
            })
            .Produces<PortfolioDto>()
            .WithTags("PortfolioEndpoints");
    }

    public async Task<IResult> HandleAsync(PortfolioRequest request, PortfolioService portfolio, ShopperResolver shoppers)
    {
        var shopper = await shoppers.RequireAsync(request.User);
        return Results.Ok(await portfolio.GetAsync(shopper));
    }
}
