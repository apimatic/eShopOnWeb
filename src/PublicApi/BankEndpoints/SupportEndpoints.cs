using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Plaid;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

/// <summary>The shop's own copy of the activity on every bank a shopper connected, synced on read.</summary>
public class BankActivityEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/bank-activity",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (string email, CatalogContext db, BankSyncService sync, CancellationToken ct) =>
                {
                    var key = email.Trim().ToLowerInvariant();
                    var banks = await db.BankConnections
                        .Where(c => c.BuyerId.ToLower() == key && c.Kind == BankConnectionKind.Bank)
                        .OrderBy(c => c.Id).ToListAsync(ct);

                    var errors = new List<object>();
                    var failures = new List<PlaidException>();
                    foreach (var bank in banks)
                    {
                        try
                        {
                            await sync.SyncAsync(bank, ct);
                        }
                        catch (PlaidException ex)
                        {
                            failures.Add(ex);
                            errors.Add(new { connectionId = bank.Id, institution = bank.InstitutionName, message = ex.Message });
                        }
                    }
                    if (banks.Count > 0 && failures.Count == banks.Count)
                        return BankApi.Fail(failures[0].ResponseStatus, failures[0].Message);

                    var ids = banks.Select(b => b.Id).ToList();
                    var rows = await db.BankTransactions.AsNoTracking().Where(t => ids.Contains(t.ConnectionId)).ToListAsync(ct);
                    var transactions = rows
                        .OrderByDescending(t => t.Date, StringComparer.Ordinal).ThenBy(t => t.Id)
                        .Select(t => new
                        {
                            connectionId = t.ConnectionId,
                            accountId = t.AccountId,
                            name = t.Name,
                            amount = t.Amount,
                            date = t.Date,
                            pending = t.Pending
                        }).ToList();
                    return Results.Ok(new { email, transactions, errors });
                })
            .WithTags("BankEndpoints");
    }
}

/// <summary>Transactions between two dates, straight from the bank, on every bank the shopper connected.</summary>
public class BankTransactionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/transactions",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (string email, string? from, string? to, CatalogContext db, PlaidClient plaid, CancellationToken ct) =>
                {
                    if (!TryDate(from, out var start) || !TryDate(to, out var end))
                        return BankApi.Fail(400, "from and to are required, as YYYY-MM-DD dates.");
                    if (start > end) return BankApi.Fail(400, "from must not be after to.");

                    var key = email.Trim().ToLowerInvariant();
                    var banks = await db.BankConnections.AsNoTracking()
                        .Where(c => c.BuyerId.ToLower() == key && c.Kind == BankConnectionKind.Bank)
                        .OrderBy(c => c.Id).ToListAsync(ct);

                    var transactions = new List<object>();
                    var errors = new List<object>();
                    var failures = new List<PlaidException>();
                    foreach (var bank in banks)
                    {
                        try
                        {
                            foreach (var t in await FetchAsync(plaid, bank, start, end, ct))
                            {
                                transactions.Add(new
                                {
                                    connectionId = bank.Id,
                                    accountId = t.Str("account_id"),
                                    name = t.Str("name"),
                                    amount = t.Dec("amount"),
                                    date = t.Str("date")
                                });
                            }
                        }
                        catch (PlaidException ex)
                        {
                            failures.Add(ex);
                            errors.Add(new { connectionId = bank.Id, institution = bank.InstitutionName, message = ex.Message });
                        }
                    }
                    if (banks.Count > 0 && failures.Count == banks.Count)
                        return BankApi.Fail(failures[0].ResponseStatus, failures[0].Message);
                    return Results.Ok(new { email, from, to, transactions, errors });
                })
            .WithTags("BankEndpoints");
    }

    private static bool TryDate(string? s, out DateOnly date) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static async Task<List<JsonNode>> FetchAsync(PlaidClient plaid, BankConnection bank, DateOnly start, DateOnly end, CancellationToken ct)
    {
        const int pageSize = 500;
        var result = new List<JsonNode>();
        while (true)
        {
            var offset = result.Count;
            // An item connected before this lookup existed has no transactions yet: Plaid prepares them on the
            // first request, answering PRODUCT_NOT_READY meanwhile.
            var page = await BankApi.WhenProductReady(() => plaid.PostAsync("/transactions/get", new JsonObject
            {
                ["access_token"] = bank.AccessToken,
                ["start_date"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["end_date"] = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["options"] = new JsonObject { ["count"] = pageSize, ["offset"] = offset }
            }, ct), ct);

            var items = page.Arr("transactions").Where(t => t is not null).Select(t => t!).ToList();
            result.AddRange(items);
            var total = page["total_transactions"]?.GetValue<int>() ?? 0;
            if (items.Count == 0 || result.Count >= total) break;
        }
        return result;
    }
}

/// <summary>The holdings of the caller's connected brokerage accounts.</summary>
public class PortfolioEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/portfolio",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (ClaimsPrincipal user, CatalogContext db, PlaidClient plaid, CancellationToken ct) =>
                {
                    var buyer = BankApi.UserName(user);
                    if (buyer is null) return BankApi.Fail(401, "The token does not identify a user.");

                    var accounts = await db.BankConnections.AsNoTracking()
                        .Where(c => c.BuyerId == buyer && c.Kind == BankConnectionKind.Brokerage)
                        .OrderBy(c => c.Id).ToListAsync(ct);

                    var positions = new List<Dictionary<string, object?>>();
                    var errors = new List<object>();
                    var failures = new List<PlaidException>();
                    foreach (var account in accounts)
                    {
                        try
                        {
                            var answer = await BankApi.WhenProductReady(() => plaid.PostAsync("/investments/holdings/get",
                                new JsonObject { ["access_token"] = account.AccessToken }, ct), ct);
                            var securities = answer.Arr("securities").Where(s => s is not null)
                                .ToDictionary(s => s.Str("security_id") ?? "", s => s!);
                            foreach (var holding in answer.Arr("holdings").Where(h => h is not null))
                            {
                                securities.TryGetValue(holding.Str("security_id") ?? "", out var security);
                                positions.Add(ToPosition(account, holding!, security));
                            }
                        }
                        catch (PlaidException ex)
                        {
                            failures.Add(ex);
                            errors.Add(new { connectionId = account.Id, institution = account.InstitutionName, message = ex.Message });
                        }
                    }
                    if (accounts.Count > 0 && failures.Count == accounts.Count)
                        return BankApi.Fail(failures[0].ResponseStatus, failures[0].Message);
                    return Results.Ok(new { positions, errors });
                })
            .WithTags("BankEndpoints");
    }

    /// <summary>One option contract covers this many shares of the underlying.</summary>
    private const decimal SharesPerContract = 100m;

    private static bool IsOption(JsonNode? security) =>
        security is not null && (security["option_contract"] is JsonObject || security.Str("type") == "derivative");
    private static Dictionary<string, object?> ToPosition(BankConnection account, JsonNode holding, JsonNode? security)
    {
        var quantity = holding.Dec("quantity") ?? 0m;
        var position = new Dictionary<string, object?>
        {
            ["connectionId"] = account.Id,
            ["accountId"] = holding.Str("account_id"),
            ["ticker"] = security.Str("ticker_symbol"),
            ["name"] = security.Str("name"),
            ["type"] = security.Str("type"),
            ["value"] = holding.Dec("institution_value") ?? 0m
        };
        // Plaid counts an option's quantity in shares (value = quantity x price); the shopper holds contracts of 100 shares
        if (IsOption(security)) position["contracts"] = quantity / SharesPerContract;
        else position["quantity"] = quantity;
        return position;
    }
}
