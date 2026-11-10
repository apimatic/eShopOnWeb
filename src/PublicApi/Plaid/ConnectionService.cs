using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public record LinkTokenRequest(string? Type);
public record SaveConnectionRequest(string? PublicToken, string? Type);

/// <summary>Link tokens, saving, listing and disconnecting bank / brokerage connections.</summary>
public class ConnectionService
{
    private readonly PlaidClient _plaid;
    private readonly CatalogContext _db;
    private readonly IDataProtector _protector;

    public ConnectionService(PlaidClient plaid, CatalogContext db, IDataProtectionProvider dataProtection)
    {
        _plaid = plaid;
        _db = db;
        _protector = dataProtection.CreateProtector("Plaid.AccessToken");
    }

    /// <summary>Plaid forbids personal data such as an email in client_user_id, so send a stable hash.</summary>
    public static string OpaqueUserId(string userName) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ApiResults.OwnerKey(userName)))).ToLowerInvariant();

    public string Unprotect(BankConnection c) => _protector.Unprotect(c.ProtectedAccessToken);

    public static bool TryParseKind(string? type, out string kind)
    {
        kind = (type ?? ConnectionKinds.Bank).Trim().ToLowerInvariant();
        return kind == ConnectionKinds.Bank || kind == ConnectionKinds.Brokerage;
    }

    public async Task<IResult> CreateLinkTokenAsync(string userName, LinkTokenRequest? request, CancellationToken ct)
    {
        if (!TryParseKind(request?.Type, out var kind))
            return ApiResults.BadRequest("type must be 'bank' or 'brokerage'.");

        var body = new Dictionary<string, object?>
        {
            ["client_name"] = "eShopOnWeb",
            ["language"] = "en",
            ["country_codes"] = BankPickerService.CountryCodes,
            ["user"] = new Dictionary<string, object> { ["client_user_id"] = OpaqueUserId(userName) }
        };
        if (kind == ConnectionKinds.Brokerage)
        {
            body["products"] = new[] { "investments" };
        }
        else
        {
            // Payment is the product; transaction history is consented up front so support can read it later.
            body["products"] = new[] { "transfer" };
            body["additional_consented_products"] = new[] { "transactions" };
        }

        try
        {
            var result = await _plaid.LinkTokenCreateAsync(body, ct);
            return Results.Ok(new { linkToken = result.Str("link_token"), expiration = result.Str("expiration"), type = kind });
        }
        catch (PlaidException ex)
        {
            return ApiResults.FromPlaid(ex);
        }
    }

    public async Task<IResult> SaveAsync(string userName, SaveConnectionRequest? request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.PublicToken))
            return ApiResults.BadRequest("publicToken is required.");
        if (!TryParseKind(request.Type, out var kind))
            return ApiResults.BadRequest("type must be 'bank' or 'brokerage'.");

        var owner = ApiResults.OwnerKey(userName);
        try
        {
            var exchange = await _plaid.ExchangePublicTokenAsync(request.PublicToken.Trim(), ct);
            var accessToken = exchange.Str("access_token");
            var itemId = exchange.Str("item_id");
            if (accessToken is null || itemId is null)
                return ApiResults.Error(StatusCodes.Status502BadGateway, "Plaid did not return an access token.");

            var accounts = await _plaid.AccountsGetAsync(accessToken, ct);
            var accountList = accounts.Arr("accounts").ToList();

            var wanted = kind == ConnectionKinds.Brokerage ? "investment" : "depository";
            if (!accountList.Any(a => a.Str("type") == wanted))
            {
                // Nothing usable was connected: stop paying for the Item.
                await TryRemoveAsync(accessToken, ct);
                return ApiResults.BadRequest(kind == ConnectionKinds.Brokerage
                    ? "The connection has no brokerage (investment) account."
                    : "The connection has no checking or savings account to pay from.");
            }

            string? institutionId = accounts.Obj("item")?.Str("institution_id");
            string? institutionName = null;
            if (institutionId is not null)
            {
                try
                {
                    var inst = await _plaid.PostAsync("institutions/get_by_id", new()
                    {
                        ["institution_id"] = institutionId,
                        ["country_codes"] = BankPickerService.CountryCodes
                    }, ct);
                    institutionName = inst.Obj("institution")?.Str("name");
                }
                catch (PlaidException) { /* the name is cosmetic */ }
            }

            var existing = await _db.BankConnections.Include(c => c.Accounts)
                .FirstOrDefaultAsync(c => c.ItemId == itemId, ct);
            if (existing is not null && existing.OwnerKey != owner)
                return ApiResults.Conflict("This bank connection already belongs to another shopper.");

            var connection = existing ?? new BankConnection { OwnerKey = owner, ItemId = itemId };
            connection.Kind = kind;
            connection.ProtectedAccessToken = _protector.Protect(accessToken);
            connection.InstitutionId = institutionId;
            connection.InstitutionName = institutionName ?? connection.InstitutionName;
            connection.Accounts.Clear();
            foreach (var a in accountList)
            {
                connection.Accounts.Add(new BankConnectionAccount
                {
                    AccountId = a.Str("account_id") ?? string.Empty,
                    Name = a.Str("name"),
                    Mask = a.Str("mask"),
                    Type = a.Str("type"),
                    Subtype = a.Str("subtype")
                });
            }
            if (existing is null) _db.BankConnections.Add(connection);
            await _db.SaveChangesAsync(ct);

            var dto = ToDto(connection);
            return existing is null
                ? Results.Created($"api/bank/connections/{connection.PublicId}", dto)
                : Results.Ok(dto);
        }
        catch (PlaidException ex)
        {
            return ApiResults.FromPlaid(ex);
        }
    }

    public async Task<IResult> ListAsync(string userName, CancellationToken ct)
    {
        var owner = ApiResults.OwnerKey(userName);
        var list = await _db.BankConnections.Include(c => c.Accounts)
            .Where(c => c.OwnerKey == owner).OrderBy(c => c.Id).ToListAsync(ct);
        return Results.Ok(new { connections = list.Select(ToDto) });
    }

    public async Task<IResult> DisconnectAsync(string userName, string connectionId, CancellationToken ct)
    {
        var owner = ApiResults.OwnerKey(userName);
        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.PublicId == connectionId && c.OwnerKey == owner, ct);
        if (connection is null)
            return ApiResults.NotFound("Connection not found.");

        try
        {
            // Removing the Item at Plaid is what ends the billing for it.
            await _plaid.ItemRemoveAsync(Unprotect(connection), ct);
        }
        catch (PlaidException ex) when (ex.ErrorCode == "ITEM_NOT_FOUND")
        {
            // Already gone at Plaid: nothing left to pay for.
        }
        catch (PlaidException ex)
        {
            return ApiResults.FromPlaid(ex);
        }

        // Checkouts still open on this connection can no longer be paid.
        var open = await _db.BankCheckouts
            .Where(c => c.BankConnectionId == connection.Id && c.Status == CheckoutStatuses.Open).ToListAsync(ct);
        _db.BankCheckouts.RemoveRange(open);
        _db.BankConnections.Remove(connection);
        await _db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Finds a connection of the owner by its public id.</summary>
    public Task<BankConnection?> FindOwnedAsync(string owner, string connectionId, CancellationToken ct) =>
        _db.BankConnections.Include(c => c.Accounts)
            .FirstOrDefaultAsync(c => c.PublicId == connectionId && c.OwnerKey == owner, ct);

    private async Task TryRemoveAsync(string accessToken, CancellationToken ct)
    {
        try { await _plaid.ItemRemoveAsync(accessToken, ct); }
        catch (PlaidException) { }
    }

    private static object ToDto(BankConnection c) => new
    {
        connectionId = c.PublicId,
        type = c.Kind,
        institutionId = c.InstitutionId,
        institutionName = c.InstitutionName,
        createdAt = c.CreatedAt,
        accounts = c.Accounts.Select(a => new { accountId = a.AccountId, a.Name, a.Mask, a.Type, a.Subtype })
    };
}
