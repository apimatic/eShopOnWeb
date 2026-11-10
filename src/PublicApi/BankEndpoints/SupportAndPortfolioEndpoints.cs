using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

public record BankTransactionDto(
    string TransactionId,
    int ConnectionId,
    string? Bank,
    string AccountId,
    string Name,
    decimal Amount,
    DateOnly Date,
    bool Pending)
{
    public static BankTransactionDto From(ActivityTransaction t) =>
        new(t.TransactionId, t.ConnectionId, t.Bank, t.AccountId, t.Name, t.Amount, DateOnly.FromDateTime(t.Date), t.Pending);
}

public record BankTransactionsResponse(string Email, IReadOnlyList<BankTransactionDto> Transactions);

public class BankActivityRequest : BaseRequest
{
    public string Email { get; init; } = string.Empty;
}

/// <summary>
/// The activity on a shopper's connected banks, from the shop's own copy kept in step with the banks' records
/// </summary>
public class BankActivityEndpoint : IEndpoint<IResult, BankActivityRequest, BankActivityService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/bank-activity",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, BankActivityService service) =>
            {
                return await HandleAsync(new BankActivityRequest { Email = email }, service);
            })
            .Produces<BankTransactionsResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(BankActivityRequest request, BankActivityService service)
    {
        var items = await service.GetActivityAsync(request.Email, default);
        return Results.Ok(new BankTransactionsResponse(request.Email, items.Select(BankTransactionDto.From).ToList()));
    }
}

public class BankTransactionsRequest : BaseRequest
{
    public string Email { get; init; } = string.Empty;
    public string? From { get; init; }
    public string? To { get; init; }
}

/// <summary>
/// The transactions on a shopper's connected banks between two dates, straight from the banks
/// </summary>
public class BankTransactionsEndpoint : IEndpoint<IResult, BankTransactionsRequest, BankActivityService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/transactions",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, string? from, string? to, BankActivityService service) =>
            {
                return await HandleAsync(new BankTransactionsRequest { Email = email, From = from, To = to }, service);
            })
            .Produces<BankTransactionsResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(BankTransactionsRequest request, BankActivityService service)
    {
        var from = ParseDate(request.From, "from");
        var to = ParseDate(request.To, "to");
        var items = await service.GetTransactionsAsync(request.Email, from, to, default);
        return Results.Ok(new BankTransactionsResponse(request.Email, items.Select(BankTransactionDto.From).ToList()));
    }

    private static DateTime ParseDate(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new BankingException(400, $"'{name}' is required (YYYY-MM-DD).");
        if (!DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new BankingException(400, $"'{name}' must be a date formatted YYYY-MM-DD.");
        }
        return date;
    }
}

public class PortfolioRequest : BaseRequest
{
    public string BuyerId { get; init; } = string.Empty;
}

public record PortfolioPositionDto(
    string? Ticker,
    string? Name,
    string? Type,
    decimal Value,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] decimal? Quantity,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] decimal? Contracts,
    string AccountId,
    string? Institution)
{
    public static PortfolioPositionDto From(Position p) =>
        new(p.Ticker, p.Name, p.Type, p.Value, p.Quantity, p.Contracts, p.AccountId, p.Institution);
}

public record PortfolioResponse(IReadOnlyList<PortfolioPositionDto> Positions);

/// <summary>
/// The holdings of the caller's connected brokerage account(s)
/// </summary>
public class PortfolioEndpoint : IEndpoint<IResult, PortfolioRequest, PortfolioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/portfolio",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, PortfolioService service) =>
            {
                return await HandleAsync(new PortfolioRequest { BuyerId = BankEndpointSupport.Caller(user) }, service);
            })
            .Produces<PortfolioResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(PortfolioRequest request, PortfolioService service)
    {
        var positions = await service.GetAsync(request.BuyerId, default);
        return Results.Ok(new PortfolioResponse(positions.Select(PortfolioPositionDto.From).ToList()));
    }
}
