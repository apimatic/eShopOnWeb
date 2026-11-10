using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupportEndpoints;

public record TransactionDto(int ConnectionId, string AccountId, string Name, decimal Amount, DateOnly Date, bool Pending);

public record ProblemDto(int ConnectionId, string Reason);

public class TransactionsResponse : BaseResponse
{
    public List<TransactionDto> Transactions { get; set; } = new();

    /// <summary>Connected banks that could not be read, with the reason.</summary>
    public List<ProblemDto> Problems { get; set; } = new();

    public static TransactionsResponse From(ActivityResult result)
    {
        var response = new TransactionsResponse();
        foreach (var t in result.Transactions)
            response.Transactions.Add(new TransactionDto(t.ConnectionId, t.AccountId, t.Name, t.Amount, t.Date, t.Pending));
        foreach (var p in result.Problems)
            response.Problems.Add(new ProblemDto(p.ConnectionId, p.Reason));
        return response;
    }
}

/// <summary>
/// The shop's own copy of the activity on a shopper's connected banks, for support staff
/// </summary>
public class BankActivityEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/bank-activity",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, BankActivityService activity, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(email, activity, cancellationToken);
            })
            .Produces<TransactionsResponse>()
            .WithTags("SupportEndpoints");
    }

    public async Task<IResult> HandleAsync(string email, BankActivityService activity, CancellationToken cancellationToken)
    {
        var result = await activity.GetActivityAsync(UserExtensions.NormalizeShopper(email), cancellationToken);
        return Results.Ok(TransactionsResponse.From(result));
    }
}

/// <summary>
/// Transactions on a shopper's connected banks between two dates, straight from the bank
/// </summary>
public class BankTransactionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/transactions",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, string? from, string? to, BankActivityService activity, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(email, from, to, activity, cancellationToken);
            })
            .Produces<TransactionsResponse>()
            .WithTags("SupportEndpoints");
    }

    public async Task<IResult> HandleAsync(string email, string? from, string? to, BankActivityService activity,
        CancellationToken cancellationToken)
    {
        var start = ParseDate(from, "from");
        var end = ParseDate(to, "to");
        if (start > end)
            throw BankingException.BadRequest("from must not be after to.");

        var result = await activity.GetTransactionsBetweenAsync(UserExtensions.NormalizeShopper(email), start, end, cancellationToken);
        return Results.Ok(TransactionsResponse.From(result));
    }

    private static DateOnly ParseDate(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw BankingException.BadRequest($"{name} is required (YYYY-MM-DD).");
        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw BankingException.BadRequest($"{name} must be a date formatted YYYY-MM-DD.");
        return date;
    }
}
