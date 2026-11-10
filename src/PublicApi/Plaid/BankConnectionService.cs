using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public record BankAccountInfo(string Id, string Name, string? Mask, string Type, string? Subtype);

public static class Buyer
{
    /// <summary>The shop identifies shoppers by their (case-insensitive) email.</summary>
    public static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static IReadOnlyList<BankAccountInfo> ReadAccounts(BankConnection connection) =>
        JsonSerializer.Deserialize<List<BankAccountInfo>>(connection.AccountsJson) ?? new List<BankAccountInfo>();
}

public class BankConnectionService
{
    private readonly CatalogContext _db;
    private readonly IPlaidClient _plaid;

    public BankConnectionService(CatalogContext db, IPlaidClient plaid)
    {
        _db = db;
        _plaid = plaid;
    }

    public async Task<(string LinkToken, string Expiration)> CreateLinkTokenAsync(string buyer, string? type)
    {
        var kind = (type ?? "bank").Trim().ToLowerInvariant();
        if (kind is not ("bank" or "brokerage"))
        {
            throw new ApiException(400, "type must be 'bank' or 'brokerage'.");
        }

        // Plaid asks that client_user_id is not personally identifiable, so the email is hashed.
        var clientUserId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyer))).ToLowerInvariant();
        var body = new JsonObject
        {
            ["client_name"] = "eShopOnWeb",
            ["language"] = "en",
            ["country_codes"] = new JsonArray("US"),
            ["user"] = new JsonObject { ["client_user_id"] = clientUserId },
        };
        if (kind == "bank")
        {
            body["products"] = new JsonArray("auth");
            body["optional_products"] = new JsonArray("transactions");
        }
        else
        {
            body["products"] = new JsonArray("investments");
        }

        var response = await _plaid.PostAsync("/link/token/create", body);
        return (response["link_token"]!.GetValue<string>(), response["expiration"]?.GetValue<string>() ?? string.Empty);
    }

    public async Task<BankConnection> SaveAsync(string buyer, string? publicToken)
    {
        if (string.IsNullOrWhiteSpace(publicToken))
        {
            throw new ApiException(400, "publicToken is required.");
        }

        var exchange = await _plaid.PostAsync("/item/public_token/exchange", new JsonObject { ["public_token"] = publicToken.Trim() });
        var accessToken = exchange["access_token"]!.GetValue<string>();
        var itemId = exchange["item_id"]!.GetValue<string>();

        var itemKnown = false;
        try
        {
            var existingItem = await _db.BankConnections.FirstOrDefaultAsync(c => c.ItemId == itemId);
            if (existingItem != null)
            {
                itemKnown = true;
                if (existingItem.BuyerId != buyer)
                {
                    throw new ApiException(409, "This bank connection already belongs to another shopper.");
                }
                return existingItem;
            }

            var accountsResponse = await _plaid.PostAsync("/accounts/get", new JsonObject { ["access_token"] = accessToken });
            var accounts = accountsResponse["accounts"]!.AsArray().Select(a => new BankAccountInfo(
                a!["account_id"]!.GetValue<string>(),
                a["name"]?.GetValue<string>() ?? a["official_name"]?.GetValue<string>() ?? "Account",
                a["mask"]?.GetValue<string>(),
                a["type"]?.GetValue<string>() ?? "other",
                a["subtype"]?.GetValue<string>())).ToList();

            var hasBank = accounts.Any(a => a.Type == "depository");
            var hasInvestment = accounts.Any(a => a.Type == "investment");
            if (!hasBank && !hasInvestment)
            {
                throw new ApiException(422, "The connected account is neither a bank account nor a brokerage account.");
            }

            string institutionId = string.Empty, institutionName = string.Empty;
            var item = await _plaid.PostAsync("/item/get", new JsonObject { ["access_token"] = accessToken });
            institutionId = item["item"]?["institution_id"]?.GetValue<string>() ?? string.Empty;
            if (institutionId.Length > 0)
            {
                try
                {
                    var inst = await _plaid.PostAsync("/institutions/get_by_id", new JsonObject
                    {
                        ["institution_id"] = institutionId,
                        ["country_codes"] = new JsonArray("US"),
                    });
                    institutionName = inst["institution"]?["name"]?.GetValue<string>() ?? string.Empty;
                }
                catch (PlaidApiException)
                {
                    // The name is cosmetic.
                }
            }

            // Connecting the same accounts twice would make the shop pay Plaid for both items.
            var sameBank = await _db.BankConnections
                .Where(c => c.BuyerId == buyer && c.InstitutionId == institutionId).ToListAsync();
            var signature = Signature(accounts);
            var duplicate = institutionId.Length > 0
                ? sameBank.FirstOrDefault(c => Signature(Buyer.ReadAccounts(c)) == signature)
                : null;
            if (duplicate != null)
            {
                throw new ApiException(409, $"These accounts are already connected (connection {duplicate.Id}).");
            }

            var connection = new BankConnection
            {
                BuyerId = buyer,
                ItemId = itemId,
                AccessToken = accessToken,
                InstitutionId = institutionId,
                InstitutionName = institutionName,
                HasBankAccounts = hasBank,
                HasInvestmentAccounts = hasInvestment,
                AccountsJson = JsonSerializer.Serialize(accounts),
            };
            _db.BankConnections.Add(connection);
            await _db.SaveChangesAsync();
            return connection;
        }
        catch (Exception) when (!itemKnown)
        {
            // Not kept: stop paying Plaid for an item the shop will never use.
            await TryRemoveItemAsync(accessToken);
            throw;
        }
    }

    public async Task DisconnectAsync(string buyer, int connectionId)
    {
        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == connectionId && c.BuyerId == buyer);
        if (connection == null)
        {
            throw new ApiException(404, "Connection not found.");
        }

        try
        {
            await _plaid.PostAsync("/item/remove", new JsonObject { ["access_token"] = connection.AccessToken });
        }
        catch (PlaidApiException ex) when (ex.ErrorCode is "ITEM_NOT_FOUND" or "INVALID_ACCESS_TOKEN")
        {
            // Plaid no longer has the item, so there is nothing left to pay for.
        }

        var transactions = await _db.BankTransactions.Where(t => t.ConnectionId == connection.Id).ToListAsync();
        _db.BankTransactions.RemoveRange(transactions);
        _db.BankConnections.Remove(connection);
        await _db.SaveChangesAsync();
    }

    private async Task TryRemoveItemAsync(string accessToken)
    {
        try
        {
            await _plaid.PostAsync("/item/remove", new JsonObject { ["access_token"] = accessToken });
        }
        catch (Exception)
        {
        }
    }

    private static string Signature(IEnumerable<BankAccountInfo> accounts) =>
        string.Join("|", accounts.Select(a => $"{a.Name}/{a.Mask}/{a.Subtype}").OrderBy(s => s, StringComparer.Ordinal));
}
