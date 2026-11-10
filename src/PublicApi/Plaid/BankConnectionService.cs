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

public class ConnectionDto
{
    public int ConnectionId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? Mask { get; set; }
}

public class BankConnectionService
{
    private static readonly string[] BankProducts = { "auth", "transfer" };

    private readonly PlaidClient _plaid;
    private readonly CatalogContext _db;

    public BankConnectionService(PlaidClient plaid, CatalogContext db)
    {
        _plaid = plaid;
        _db = db;
    }

    public static string NormalizeBuyer(string name) => name.Trim().ToLowerInvariant();

    public static ConnectionDto ToDto(BankConnection c) => new()
    {
        ConnectionId = c.Id,
        Type = c.Type,
        InstitutionId = c.InstitutionId,
        InstitutionName = c.InstitutionName,
        AccountId = c.AccountId,
        AccountName = c.AccountName,
        Mask = c.AccountMask
    };

    public static string ParseType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return BankConnectionTypes.Bank;
        }

        return type.Trim().ToLowerInvariant() switch
        {
            "bank" => BankConnectionTypes.Bank,
            "brokerage" => BankConnectionTypes.Brokerage,
            _ => throw ApiProblemException.BadRequest("type must be 'bank' or 'brokerage'.")
        };
    }

    public async Task<List<ConnectionDto>> ListAsync(string buyer, CancellationToken ct)
    {
        var id = NormalizeBuyer(buyer);
        var list = await _db.BankConnections.Where(c => c.BuyerId == id).OrderBy(c => c.Id).ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<BankConnection> GetOwnedAsync(string buyer, int connectionId, CancellationToken ct)
    {
        var id = NormalizeBuyer(buyer);
        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == connectionId && c.BuyerId == id, ct);
        return connection ?? throw ApiProblemException.NotFound($"Connection {connectionId} was not found.");
    }

    /// <summary>What the browser needs to open Plaid Link.</summary>
    public async Task<(string LinkToken, string? Expiration)> CreateLinkTokenAsync(
        string buyer, string? type, string? institutionId, int? connectionId, CancellationToken ct)
    {
        var kind = ParseType(type);
        var clientUserId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeBuyer(buyer)))).ToLowerInvariant();

        var request = new JsonObject
        {
            ["client_name"] = "eShopOnWeb",
            ["language"] = "en",
            ["country_codes"] = new JsonArray(BankDirectory.CountryCodes.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
            ["user"] = new JsonObject { ["client_user_id"] = clientUserId }
        };

        if (connectionId is not null)
        {
            // Update mode: re-open Link for a connection that exists already (for example to grant
            // the consent needed for transactions on a bank connected before that was requested).
            var existing = await GetOwnedAsync(buyer, connectionId.Value, ct);
            request["access_token"] = existing.AccessToken;
            if (existing.Type == BankConnectionTypes.Bank)
            {
                request["additional_consented_products"] = new JsonArray("transactions");
            }
        }
        else if (kind == BankConnectionTypes.Brokerage)
        {
            request["products"] = new JsonArray("investments");
            request["account_filters"] = new JsonObject
            {
                ["investment"] = new JsonObject { ["account_subtypes"] = new JsonArray("all") }
            };
        }
        else
        {
            request["products"] = new JsonArray("auth");
            request["additional_consented_products"] = new JsonArray("transactions");
            request["transactions"] = new JsonObject { ["days_requested"] = 730 };
            request["account_filters"] = new JsonObject
            {
                ["depository"] = new JsonObject { ["account_subtypes"] = new JsonArray("checking", "savings") }
            };
        }

        if (!string.IsNullOrWhiteSpace(institutionId) && connectionId is null)
        {
            request["institution_id"] = institutionId.Trim();
        }

        var json = await _plaid.PostAsync("/link/token/create", request, ct);
        return (json["link_token"]!.GetValue<string>(), json["expiration"]?.GetValue<string>());
    }

    /// <summary>Saves the connection Link produced: exchanges the public token and stores the Item.</summary>
    public async Task<ConnectionDto> ConnectAsync(string buyer, string? publicToken, string? type, string? accountId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(publicToken))
        {
            throw ApiProblemException.BadRequest("publicToken is required.");
        }

        var kind = ParseType(type);
        var buyerId = NormalizeBuyer(buyer);

        var exchange = await _plaid.PostAsync("/item/public_token/exchange", new { public_token = publicToken.Trim() }, ct);
        var accessToken = exchange["access_token"]!.GetValue<string>();
        var itemId = exchange["item_id"]!.GetValue<string>();

        var duplicate = await _db.BankConnections.FirstOrDefaultAsync(c => c.ItemId == itemId, ct);
        if (duplicate is not null)
        {
            if (duplicate.BuyerId == buyerId)
            {
                return ToDto(duplicate);
            }

            throw ApiProblemException.Conflict("This connection belongs to another shopper.");
        }

        try
        {
            var item = (await _plaid.PostAsync("/item/get", new { access_token = accessToken }, ct))["item"]!;
            var accounts = (await _plaid.PostAsync("/accounts/get", new { access_token = accessToken }, ct))["accounts"]!.AsArray();
            var products = new HashSet<string>(
                ((item["products"]?.AsArray() ?? new JsonArray()).Concat(item["billed_products"]?.AsArray() ?? new JsonArray()))
                    .Select(p => p!.GetValue<string>()));

            var connection = new BankConnection
            {
                BuyerId = buyerId,
                Type = kind,
                ItemId = itemId,
                AccessToken = accessToken,
                InstitutionId = item["institution_id"]?.GetValue<string>()
            };

            if (kind == BankConnectionTypes.Bank)
            {
                if (!BankProducts.Any(products.Contains))
                {
                    throw ApiProblemException.BadRequest("This account was not connected for payments; connect it as a bank account through Link.");
                }

                var account = PickPaymentAccount(accounts, accountId);
                connection.AccountId = account["account_id"]!.GetValue<string>();
                connection.AccountName = account["name"]?.GetValue<string>();
                connection.AccountMask = account["mask"]?.GetValue<string>();
            }
            else
            {
                if (!products.Contains("investments") || !accounts.Any(a => a!["type"]?.GetValue<string>() == "investment"))
                {
                    throw ApiProblemException.BadRequest("This connection has no brokerage (investment) account.");
                }
            }

            connection.InstitutionName = await GetInstitutionNameAsync(connection.InstitutionId, ct);

            _db.BankConnections.Add(connection);
            await _db.SaveChangesAsync(ct);
            return ToDto(connection);
        }
        catch
        {
            // Never keep paying Plaid for an Item the shop does not keep.
            await TryRemoveItemAsync(accessToken);
            throw;
        }
    }

    private static JsonNode PickPaymentAccount(JsonArray accounts, string? accountId)
    {
        var depository = accounts.Where(a => a!["type"]?.GetValue<string>() == "depository").Select(a => a!).ToList();
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            return depository.FirstOrDefault(a => a["account_id"]?.GetValue<string>() == accountId.Trim())
                ?? throw ApiProblemException.BadRequest("accountId is not a bank (depository) account of this connection.");
        }

        return depository.FirstOrDefault(a => a["subtype"]?.GetValue<string>() == "checking")
            ?? depository.FirstOrDefault(a => a["subtype"]?.GetValue<string>() == "savings")
            ?? depository.FirstOrDefault()
            ?? throw ApiProblemException.BadRequest("This connection has no bank (depository) account to pay from.");
    }

    private async Task<string?> GetInstitutionNameAsync(string? institutionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(institutionId))
        {
            return null;
        }

        try
        {
            var json = await _plaid.PostAsync("/institutions/get_by_id", new
            {
                institution_id = institutionId,
                country_codes = BankDirectory.CountryCodes
            }, ct);
            return json["institution"]?["name"]?.GetValue<string>();
        }
        catch (PlaidApiException)
        {
            return null; // the name is cosmetic
        }
    }

    private async Task TryRemoveItemAsync(string accessToken)
    {
        try
        {
            await _plaid.PostAsync("/item/remove", new { access_token = accessToken }, CancellationToken.None);
        }
        catch (Exception)
        {
            // best effort
        }
    }

    /// <summary>Disconnects the bank: removes the Item at Plaid (which ends billing) and then forgets it locally.</summary>
    public async Task DisconnectAsync(string buyer, int connectionId, CancellationToken ct)
    {
        var connection = await GetOwnedAsync(buyer, connectionId, ct);

        try
        {
            await _plaid.PostAsync("/item/remove", new { access_token = connection.AccessToken }, ct);
        }
        catch (PlaidApiException ex) when (ex.ErrorCode is "ITEM_NOT_FOUND" or "INVALID_ACCESS_TOKEN")
        {
            // Plaid no longer knows the Item, so nothing is being billed any more.
        }

        _db.BankTransactions.RemoveRange(_db.BankTransactions.Where(t => t.ConnectionId == connection.Id));
        _db.BankCheckouts.RemoveRange(_db.BankCheckouts.Where(c => c.ConnectionId == connection.Id && c.Status == BankCheckoutStatus.Open));
        _db.BankConnections.Remove(connection);
        await _db.SaveChangesAsync(ct);
    }
}
