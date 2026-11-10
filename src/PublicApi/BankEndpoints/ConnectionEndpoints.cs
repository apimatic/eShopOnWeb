using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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

/// <summary>Lists (and searches) the banks Plaid supports, for the checkout bank picker.</summary>
public class BankListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/banks", async (string? query, int? limit, int? offset, InstitutionService institutions, CancellationToken ct) =>
            {
                try
                {
                    var q = query?.Trim();
                    if (q is { Length: > 100 }) return BankApi.Fail(400, "query must be at most 100 characters.");
                    var banks = string.IsNullOrEmpty(q)
                        ? await institutions.ListAsync(ct)
                        : await institutions.SearchAsync(q, ct);
                    if (limit is < 1 || offset is < 0) return BankApi.Fail(400, "limit must be at least 1 and offset at least 0.");
                    var page = banks.Skip(offset ?? 0);
                    if (limit is not null) page = page.Take(limit.Value);
                    var list = page.ToList();
                    return Results.Ok(new { total = banks.Count, count = list.Count, banks = list });
                }
                catch (PlaidException ex)
                {
                    return BankApi.Fail(ex);
                }
            })
            .WithTags("BankEndpoints");
    }
}

public class LinkTokenRequest : BaseRequest
{
    /// <summary>"bank" (default) or "brokerage".</summary>
    public string? Purpose { get; set; }
}

/// <summary>Creates the Plaid Link token the browser needs to open Link.</summary>
public class CreateLinkTokenEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/link-token",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (LinkTokenRequest? request, ClaimsPrincipal user, PlaidClient plaid, CancellationToken ct) =>
                {
                    var buyer = BankApi.UserName(user);
                    if (buyer is null) return BankApi.Fail(401, "The token does not identify a user.");

                    var purpose = (request?.Purpose ?? "bank").Trim().ToLowerInvariant();
                    if (purpose is not ("bank" or "brokerage"))
                        return BankApi.Fail(400, "purpose must be 'bank' or 'brokerage'.");

                    // Plaid asks for a stable id that is not personal data, so the shopper's email is hashed.
                    var clientUserId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyer.ToLowerInvariant()))).ToLowerInvariant();
                    var body = new JsonObject
                    {
                        ["client_name"] = "eShopOnWeb",
                        ["language"] = "en",
                        ["country_codes"] = new JsonArray("US"),
                        ["user"] = new JsonObject { ["client_user_id"] = clientUserId }
                    };
                    if (purpose == "bank")
                    {
                        body["products"] = new JsonArray("auth");
                        body["required_if_supported_products"] = new JsonArray("transactions");
                        body["transactions"] = new JsonObject { ["days_requested"] = 730 };
                    }
                    else
                    {
                        body["products"] = new JsonArray("investments");
                    }

                    try
                    {
                        var answer = await plaid.PostAsync("/link/token/create", body, ct);
                        return Results.Ok(new
                        {
                            linkToken = answer.Str("link_token"),
                            expiration = answer.Str("expiration")
                        });
                    }
                    catch (PlaidException ex)
                    {
                        return BankApi.Fail(ex);
                    }
                })
            .WithTags("BankEndpoints");
    }
}

public class SaveConnectionRequest : BaseRequest
{
    public string? PublicToken { get; set; }
    public string? Public_token { get; set; }
    public string? AccountId { get; set; }
    public string? Account_id { get; set; }
    /// <summary>"bank" or "brokerage". Inferred from the linked accounts when omitted.</summary>
    public string? Purpose { get; set; }
}

public static class ConnectionDto
{
    public static object From(BankConnection c) => new
    {
        connectionId = c.Id,
        kind = c.Kind == BankConnectionKind.Bank ? "bank" : "brokerage",
        institution = c.InstitutionName,
        accountId = c.AccountId,
        accountName = c.AccountName,
        mask = c.AccountMask,
        createdAt = c.CreatedAt
    };
}

/// <summary>Saves a connection from the public token Link handed the browser.</summary>
public class SaveConnectionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/connections",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (SaveConnectionRequest? request, ClaimsPrincipal user, PlaidClient plaid, CatalogContext db, CancellationToken ct) =>
                {
                    var buyer = BankApi.UserName(user);
                    if (buyer is null) return BankApi.Fail(401, "The token does not identify a user.");

                    var publicToken = (request?.PublicToken ?? request?.Public_token)?.Trim();
                    if (string.IsNullOrEmpty(publicToken)) return BankApi.Fail(400, "publicToken is required.");
                    var wantedAccount = (request?.AccountId ?? request?.Account_id)?.Trim();
                    var purpose = request?.Purpose?.Trim().ToLowerInvariant();
                    if (purpose is not null and not ("bank" or "brokerage"))
                        return BankApi.Fail(400, "purpose must be 'bank' or 'brokerage'.");

                    string accessToken, itemId;
                    try
                    {
                        var exchanged = await plaid.PostAsync("/item/public_token/exchange",
                            new JsonObject { ["public_token"] = publicToken }, ct);
                        accessToken = exchanged.Str("access_token")!;
                        itemId = exchanged.Str("item_id")!;
                    }
                    catch (PlaidException ex)
                    {
                        return BankApi.Fail(ex);
                    }

                    try
                    {
                        var accountsAnswer = await plaid.PostAsync("/accounts/get",
                            new JsonObject { ["access_token"] = accessToken }, ct);
                        var accounts = accountsAnswer.Arr("accounts").Where(a => a is not null).Select(a => a!).ToList();

                        var depository = accounts.Where(a => a.Str("type") == "depository"
                                                             && a.Str("subtype") is "checking" or "savings").ToList();
                        var investment = accounts.Where(a => a.Str("type") == "investment").ToList();
                        var item = await ItemAsync(plaid, accessToken, ct);
                        var billed = item?.Arr("billed_products").Select(x => x?.GetValue<string>()).ToList() ?? new();
                        var kind = purpose switch
                        {
                            "bank" => BankConnectionKind.Bank,
                            "brokerage" => BankConnectionKind.Brokerage,
                            // no purpose given: an item billed only for investments is a brokerage connection
                            _ when billed.Contains("investments") && !billed.Contains("auth") && !billed.Contains("transactions")
                                => BankConnectionKind.Brokerage,
                            _ => depository.Count > 0 || investment.Count == 0 ? BankConnectionKind.Bank : BankConnectionKind.Brokerage
                        };

                        JsonNode? chosen = null;
                        if (kind == BankConnectionKind.Bank)
                        {
                            chosen = wantedAccount is not null
                                ? depository.FirstOrDefault(a => a.Str("account_id") == wantedAccount)
                                : depository.FirstOrDefault(a => a.Str("subtype") == "checking") ?? depository.FirstOrDefault();
                            if (chosen is null)
                            {
                                await RemoveItemQuietly(plaid, accessToken);
                                return BankApi.Fail(400, wantedAccount is not null
                                    ? "The chosen account is not a checking or savings account of this connection."
                                    : "The connection has no checking or savings account to pay with.");
                            }
                        }
                        else
                        {
                            if (investment.Count == 0)
                            {
                                await RemoveItemQuietly(plaid, accessToken);
                                return BankApi.Fail(400, "The connection has no brokerage account.");
                            }
                            chosen = wantedAccount is not null
                                ? investment.FirstOrDefault(a => a.Str("account_id") == wantedAccount) ?? investment[0]
                                : investment[0];
                        }

                        var connection = new BankConnection
                        {
                            BuyerId = buyer,
                            Kind = kind,
                            PlaidItemId = itemId,
                            AccessToken = accessToken,
                            AccountId = chosen.Str("account_id"),
                            AccountName = chosen.Str("name"),
                            AccountMask = chosen.Str("mask"),
                            InstitutionName = await InstitutionNameAsync(plaid, item.Str("institution_id"), ct)
                        };
                        db.BankConnections.Add(connection);
                        await db.SaveChangesAsync(ct);
                        return Results.Created($"api/bank/connections/{connection.Id}", ConnectionDto.From(connection));
                    }
                    catch (PlaidException ex)
                    {
                        await RemoveItemQuietly(plaid, accessToken);
                        return BankApi.Fail(ex);
                    }
                    catch
                    {
                        // never leave an item behind that the shop pays for but does not know about
                        await RemoveItemQuietly(plaid, accessToken);
                        throw;
                    }
                })
            .WithTags("BankEndpoints");
    }

    private static async Task RemoveItemQuietly(PlaidClient plaid, string accessToken)
    {
        try
        {
            await plaid.PostAsync("/item/remove", new JsonObject { ["access_token"] = accessToken });
        }
        catch (PlaidException)
        {
        }
    }

    private static async Task<JsonNode?> ItemAsync(PlaidClient plaid, string accessToken, CancellationToken ct)
    {
        try
        {
            return (await plaid.PostAsync("/item/get", new JsonObject { ["access_token"] = accessToken }, ct))["item"];
        }
        catch (PlaidException)
        {
            return null;
        }
    }

    private static async Task<string?> InstitutionNameAsync(PlaidClient plaid, string? institutionId, CancellationToken ct)
    {
        if (institutionId is null) return null;
        try
        {
            var inst = await plaid.PostAsync("/institutions/get_by_id", new JsonObject
            {
                ["institution_id"] = institutionId,
                ["country_codes"] = new JsonArray("US")
            }, ct);
            return inst["institution"].Str("name");
        }
        catch (PlaidException)
        {
            return null;
        }
    }
}

/// <summary>Lists the caller's own connections (so the picker can show what is connected).</summary>
public class ListConnectionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/bank/connections",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (ClaimsPrincipal user, CatalogContext db, CancellationToken ct) =>
                {
                    var buyer = BankApi.UserName(user);
                    if (buyer is null) return BankApi.Fail(401, "The token does not identify a user.");
                    var list = await db.BankConnections.Where(c => c.BuyerId == buyer).OrderBy(c => c.Id).ToListAsync(ct);
                    return Results.Ok(new { connections = list.Select(ConnectionDto.From) });
                })
            .WithTags("BankEndpoints");
    }
}

/// <summary>Disconnects a bank or brokerage account, so the shop stops paying Plaid for it.</summary>
public class DisconnectBankEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/bank/connections/{connectionId}",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int connectionId, ClaimsPrincipal user, PlaidClient plaid, CatalogContext db, CancellationToken ct) =>
                {
                    var buyer = BankApi.UserName(user);
                    if (buyer is null) return BankApi.Fail(401, "The token does not identify a user.");

                    var connection = await db.BankConnections.FirstOrDefaultAsync(c => c.Id == connectionId && c.BuyerId == buyer, ct);
                    if (connection is null) return BankApi.Fail(404, "Connection not found.");

                    try
                    {
                        // Removing the Item at Plaid is what ends the billing for it.
                        await plaid.PostAsync("/item/remove", new JsonObject { ["access_token"] = connection.AccessToken }, ct);
                    }
                    catch (PlaidException ex) when (ex.ErrorCode == "ITEM_NOT_FOUND" || ex.ErrorCode == "INVALID_ACCESS_TOKEN")
                    {
                        // already gone at Plaid
                    }
                    catch (PlaidException ex)
                    {
                        return BankApi.Fail(ex.ResponseStatus is >= 400 and < 500 ? ex.ResponseStatus : 502,
                            "The connection was not removed at Plaid, so it is still connected: " + ex.Message);
                    }

                    var copies = db.BankTransactions.Where(t => t.ConnectionId == connection.Id);
                    db.BankTransactions.RemoveRange(copies);
                    db.BankConnections.Remove(connection);
                    await db.SaveChangesAsync(ct);
                    return Results.Ok(new { connectionId, disconnected = true });
                })
            .WithTags("BankEndpoints");
    }
}
