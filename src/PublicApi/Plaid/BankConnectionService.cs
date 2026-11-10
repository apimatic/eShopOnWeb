using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Banking;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public static class CallerIdentity
{
    /// <summary>The shopper's key: the name in the JWT, normalised.</summary>
    public static string BuyerKey(ClaimsPrincipal user) => NormaliseKey(RawName(user));

    public static string RawName(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ApiException(401, "The token does not identify a shopper.");
        }
        return name;
    }

    public static string NormaliseKey(string name) => name.Trim().ToLowerInvariant();
}

/// <summary>Protects Plaid access tokens at rest.</summary>
public class AccessTokenProtector
{
    private readonly IDataProtector _protector;

    public AccessTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Plaid.AccessToken.v1");
    }

    public string Protect(string token) => _protector.Protect(token);

    public string Unprotect(BankConnection connection)
    {
        try
        {
            return _protector.Unprotect(connection.ProtectedAccessToken);
        }
        catch (CryptographicException)
        {
            throw new ApiException(409, $"The bank connection {connection.Id} can't be used any more; the shopper must connect the bank again.");
        }
    }
}

public class LinkTokenRequest
{
    /// <summary>"bank" (default) or "brokerage".</summary>
    public string? Purpose { get; set; }
}

public class ConnectBankRequest
{
    public string? PublicToken { get; set; }

    [JsonPropertyName("public_token")]
    public string? PublicTokenSnake { get; set; }

    /// <summary>"bank" (default) or "brokerage".</summary>
    public string? Purpose { get; set; }

    /// <summary>The account the shopper selected in Link (optional).</summary>
    public string? AccountId { get; set; }

    /// <summary>The metadata Link gave the browser (optional); used when the Plaid answer lacks the details.</summary>
    public JsonObject? Metadata { get; set; }
}

public class BankConnectionDto
{
    public int Id { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }
    public string? AccountName { get; set; }
    public string? AccountMask { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class BankConnectionService
{
    private readonly CatalogContext _db;
    private readonly PlaidClient _plaid;
    private readonly AccessTokenProtector _protector;

    public BankConnectionService(CatalogContext db, PlaidClient plaid, AccessTokenProtector protector)
    {
        _db = db;
        _plaid = plaid;
        _protector = protector;
    }

    public static ConnectionPurpose ParsePurpose(string? purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            return ConnectionPurpose.Bank;
        }

        return purpose.Trim().ToLowerInvariant() switch
        {
            "bank" => ConnectionPurpose.Bank,
            "brokerage" => ConnectionPurpose.Brokerage,
            _ => throw new ApiException(400, "purpose must be 'bank' or 'brokerage'.")
        };
    }

    public static BankConnectionDto ToDto(BankConnection c) => new()
    {
        Id = c.Id,
        Purpose = c.Purpose.ToString().ToLowerInvariant(),
        InstitutionId = c.InstitutionId,
        InstitutionName = c.InstitutionName,
        AccountName = c.AccountName,
        AccountMask = c.AccountMask,
        CreatedAt = c.CreatedAt
    };

    public async Task<JsonObject> CreateLinkTokenAsync(string buyerKey, ConnectionPurpose purpose)
    {
        var body = new JsonObject
        {
            ["client_name"] = "eShopOnWeb",
            ["language"] = "en",
            ["country_codes"] = new JsonArray("US"),
            // An opaque, stable id: Plaid shouldn't be given the shopper's email.
            ["user"] = new JsonObject { ["client_user_id"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerKey))).ToLowerInvariant() }
        };

        if (purpose == ConnectionPurpose.Brokerage)
        {
            body["products"] = new JsonArray("investments");
        }
        else
        {
            body["products"] = new JsonArray("auth");
            // Lets support look at the account's transactions later, billed only once they are used.
            body["additional_consented_products"] = new JsonArray("transactions");
        }

        var response = await _plaid.PostAsync("/link/token/create", body);
        return new JsonObject
        {
            ["linkToken"] = response["link_token"]?.DeepClone(),
            ["expiration"] = response["expiration"]?.DeepClone()
        };
    }

    public async Task<(BankConnectionDto Connection, bool Created)> ConnectAsync(string buyerKey, ConnectBankRequest request)
    {
        var publicToken = request.PublicToken ?? request.PublicTokenSnake;
        if (string.IsNullOrWhiteSpace(publicToken))
        {
            throw new ApiException(400, "publicToken is required.");
        }
        var purpose = ParsePurpose(request.Purpose);

        JsonObject exchange;
        try
        {
            exchange = await _plaid.PostAsync("/item/public_token/exchange", new JsonObject { ["public_token"] = publicToken });
        }
        catch (PlaidException ex) when (ex.ErrorCode is "INVALID_PUBLIC_TOKEN" or "INVALID_FIELD" or "INVALID_BODY")
        {
            throw new ApiException(400, ex.Message);
        }

        var accessToken = exchange["access_token"]!.GetValue<string>();
        var itemId = exchange["item_id"]!.GetValue<string>();

        try
        {
            var existing = await _db.BankConnections.FirstOrDefaultAsync(c => c.BuyerId == buyerKey && c.PlaidItemId == itemId);
            if (existing is not null)
            {
                return (ToDto(existing), false);
            }

            var connection = new BankConnection
            {
                BuyerId = buyerKey,
                Purpose = purpose,
                PlaidItemId = itemId,
                ProtectedAccessToken = _protector.Protect(accessToken),
            };
            await FillDetailsAsync(connection, accessToken, request);

            _db.BankConnections.Add(connection);
            await _db.SaveChangesAsync();
            return (ToDto(connection), true);
        }
        catch
        {
            // Don't keep paying Plaid for an item we could not save.
            await TryRemoveItemAsync(accessToken);
            throw;
        }
    }

    private async Task FillDetailsAsync(BankConnection connection, string accessToken, ConnectBankRequest request)
    {
        JsonObject accounts;
        try
        {
            accounts = await _plaid.PostAsync("/accounts/get", new JsonObject { ["access_token"] = accessToken });
        }
        catch (PlaidException ex) when (ex.ErrorType != "ITEM_ERROR")
        {
            throw new ApiException(400, $"The connection could not be read: {ex.Message}");
        }

        var item = accounts["item"] as JsonObject;
        var meta = request.Metadata;
        connection.InstitutionId = item?["institution_id"]?.GetValue<string>()
                                   ?? meta?["institution"]?["institution_id"]?.GetValue<string>();
        connection.InstitutionName = item?["institution_name"]?.GetValue<string>()
                                     ?? meta?["institution"]?["name"]?.GetValue<string>();

        if (connection.Purpose != ConnectionPurpose.Bank)
        {
            return;
        }

        var list = (accounts["accounts"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
        var wanted = request.AccountId
                     ?? meta?["account_id"]?.GetValue<string>()
                     ?? (meta?["accounts"] as JsonArray)?.FirstOrDefault()?["id"]?.GetValue<string>();

        JsonObject? chosen;
        if (!string.IsNullOrWhiteSpace(wanted))
        {
            chosen = list.FirstOrDefault(a => a["account_id"]?.GetValue<string>() == wanted)
                     ?? throw new ApiException(400, "accountId is not an account of this connection.");
            if (chosen["type"]?.GetValue<string>() != "depository")
            {
                throw new ApiException(400, "Only a checking or savings account can pay for an order.");
            }
        }
        else
        {
            var depository = list.Where(a => a["type"]?.GetValue<string>() == "depository").ToList();
            chosen = depository.FirstOrDefault(a => a["subtype"]?.GetValue<string>() == "checking")
                     ?? depository.FirstOrDefault()
                     ?? throw new ApiException(400, "The connection has no checking or savings account to pay with.");
        }

        connection.AccountId = chosen["account_id"]!.GetValue<string>();
        connection.AccountName = chosen["name"]?.GetValue<string>();
        connection.AccountMask = chosen["mask"]?.GetValue<string>();
    }

    private async Task TryRemoveItemAsync(string accessToken)
    {
        try
        {
            await _plaid.PostAsync("/item/remove", new JsonObject { ["access_token"] = accessToken });
        }
        catch (Exception)
        {
            // best effort
        }
    }

    public async Task<List<BankConnectionDto>> ListAsync(string buyerKey)
    {
        var list = await _db.BankConnections.AsNoTracking().Where(c => c.BuyerId == buyerKey).OrderBy(c => c.Id).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<BankConnection> GetOwnedAsync(string buyerKey, int connectionId)
    {
        return await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == connectionId && c.BuyerId == buyerKey)
               ?? throw new ApiException(404, $"Bank connection {connectionId} was not found.");
    }

    public async Task DisconnectAsync(string buyerKey, int connectionId)
    {
        var connection = await GetOwnedAsync(buyerKey, connectionId);
        var accessToken = _protector.Unprotect(connection);

        try
        {
            // Removing the item is what stops the shop paying Plaid for it.
            await _plaid.PostAsync("/item/remove", new JsonObject { ["access_token"] = accessToken });
        }
        catch (PlaidException ex) when (ex.ErrorCode == "ITEM_NOT_FOUND" || ex.ErrorCode == "INVALID_ACCESS_TOKEN")
        {
            // Plaid no longer has it; nothing more to stop paying for.
        }

        var transactions = _db.BankTransactions.Where(t => t.ConnectionId == connectionId);
        _db.BankTransactions.RemoveRange(await transactions.ToListAsync());
        _db.BankConnections.Remove(connection);
        await _db.SaveChangesAsync();
    }
}
