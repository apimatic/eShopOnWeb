using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public record LinkTokenRequest(string? Kind);
public record ConnectRequest(string? PublicToken, string? Kind, string? AccountId, string? InstitutionName);
public record ConnectionDto(Guid ConnectionId, string Kind, string? InstitutionName, string? AccountName, string? Mask, DateTimeOffset ConnectedAt);

public class ConnectionService
{
    private readonly PlaidClient _plaid;
    private readonly CatalogContext _db;

    public ConnectionService(PlaidClient plaid, CatalogContext db)
    {
        _plaid = plaid;
        _db = db;
    }

    public static string NormalizeKind(string? kind) => (kind ?? ConnectionKinds.Bank).Trim().ToLowerInvariant() switch
    {
        "bank" => ConnectionKinds.Bank,
        "brokerage" => ConnectionKinds.Brokerage,
        _ => throw new AppException(400, "kind must be 'bank' or 'brokerage'."),
    };

    public static ConnectionDto ToDto(BankConnection c) =>
        new(c.Id, c.Kind, c.InstitutionName, c.AccountName, c.AccountMask, c.CreatedAt);

    public async Task<object> CreateLinkTokenAsync(string buyerId, string? kindText, CancellationToken ct)
    {
        var kind = NormalizeKind(kindText);
        // Plaid asks for a stable, non-PII id for the user.
        var userId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId.ToLowerInvariant()))).ToLowerInvariant();

        var body = new JsonObject
        {
            ["client_name"] = "eShopOnWeb",
            ["language"] = "en",
            ["country_codes"] = new JsonArray("US"),
            ["user"] = new JsonObject { ["client_user_id"] = userId },
        };
        if (kind == ConnectionKinds.Bank)
        {
            // Auth is what lets the bank pay; Transactions is requested wherever the bank supports it for support's lookups.
            body["products"] = new JsonArray("auth");
            body["required_if_supported_products"] = new JsonArray("transactions");
        }
        else
        {
            body["products"] = new JsonArray("investments");
        }

        var res = await _plaid.PostAsync("/link/token/create", body, ct: ct);
        return new
        {
            linkToken = res["link_token"]!.GetValue<string>(),
            expiration = res["expiration"]?.GetValue<string>(),
            kind,
        };
    }

    public async Task<BankConnection> ConnectAsync(string buyerId, ConnectRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.PublicToken))
            throw new AppException(400, "publicToken is required.");
        var kind = NormalizeKind(req.Kind);

        var ex = await _plaid.PostAsync("/item/public_token/exchange", new JsonObject { ["public_token"] = req.PublicToken }, ct: ct);
        var accessToken = ex["access_token"]!.GetValue<string>();
        var itemId = ex["item_id"]!.GetValue<string>();

        // From here on the shop is paying Plaid for this Item: if we can't keep it, remove it.
        try
        {
            var existing = await _db.BankConnections.FirstOrDefaultAsync(c => c.BuyerId == buyerId && c.ItemId == itemId && c.DisconnectedAt == null, ct);
            if (existing != null) return existing;

            var accounts = (await _plaid.PostAsync("/accounts/get", new JsonObject { ["access_token"] = accessToken }, retry: true, ct))["accounts"]!.AsArray();

            JsonNode? chosen;
            if (kind == ConnectionKinds.Bank)
            {
                var depository = accounts.Where(a => a!["type"]?.GetValue<string>() == "depository").ToList();
                if (!string.IsNullOrEmpty(req.AccountId))
                {
                    chosen = depository.FirstOrDefault(a => a!["account_id"]!.GetValue<string>() == req.AccountId);
                    if (chosen == null) throw new AppException(400, "accountId is not a checking or savings account of this connection.");
                }
                else
                {
                    chosen = depository.FirstOrDefault(a => a!["subtype"]?.GetValue<string>() == "checking")
                        ?? depository.FirstOrDefault(a => a!["subtype"]?.GetValue<string>() == "savings");
                }
                if (chosen == null) throw new AppException(422, "This connection has no checking or savings account to pay from.");
            }
            else
            {
                chosen = accounts.FirstOrDefault(a => a!["type"]?.GetValue<string>() == "investment");
                if (chosen == null) throw new AppException(422, "This connection has no brokerage (investment) account.");
            }

            string? institutionId = null, institutionName = req.InstitutionName;
            try
            {
                var item = await _plaid.PostAsync("/item/get", new JsonObject { ["access_token"] = accessToken }, retry: true, ct);
                institutionId = item["item"]?["institution_id"]?.GetValue<string>();
                if (institutionId != null)
                {
                    var inst = await _plaid.PostAsync("/institutions/get_by_id", new JsonObject
                    {
                        ["institution_id"] = institutionId,
                        ["country_codes"] = new JsonArray("US"),
                    }, retry: true, ct);
                    institutionName = inst["institution"]?["name"]?.GetValue<string>() ?? institutionName;
                }
            }
            catch (PlaidApiException) { /* display name only */ }

            var conn = new BankConnection
            {
                BuyerId = buyerId,
                Kind = kind,
                ItemId = itemId,
                AccessToken = accessToken,
                InstitutionId = institutionId,
                InstitutionName = institutionName,
                AccountId = chosen["account_id"]!.GetValue<string>(),
                AccountName = chosen["name"]?.GetValue<string>(),
                AccountMask = chosen["mask"]?.GetValue<string>(),
            };
            _db.BankConnections.Add(conn);
            await _db.SaveChangesAsync(ct);
            return conn;
        }
        catch
        {
            await TryRemoveItemAsync(accessToken);
            throw;
        }
    }

    public async Task<List<BankConnection>> ListAsync(string buyerId, CancellationToken ct) =>
        await _db.BankConnections.Where(c => c.BuyerId == buyerId && c.DisconnectedAt == null)
            .OrderBy(c => c.CreatedAt).ToListAsync(ct);

    public async Task DisconnectAsync(string buyerId, Guid connectionId, CancellationToken ct)
    {
        var conn = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == connectionId && c.BuyerId == buyerId && c.DisconnectedAt == null, ct);
        if (conn == null) throw new AppException(404, "Connection not found.");

        // Removing the Item at Plaid is what stops the shop being billed for it.
        try
        {
            await _plaid.PostAsync("/item/remove", new JsonObject { ["access_token"] = conn.AccessToken }, retry: true, ct);
        }
        catch (PlaidApiException e) when (e.ErrorCode is "ITEM_NOT_FOUND" or "INVALID_ACCESS_TOKEN") { }

        var txns = await _db.BankTransactions.Where(t => t.ConnectionId == conn.Id).ToListAsync(ct);
        _db.BankTransactions.RemoveRange(txns);
        conn.AccessToken = null;
        conn.SyncCursor = null;
        conn.DisconnectedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task TryRemoveItemAsync(string accessToken)
    {
        try { await _plaid.PostAsync("/item/remove", new JsonObject { ["access_token"] = accessToken }); }
        catch (Exception) { }
    }
}
