using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.PublicApi.Plaid.Data;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Plaid.Services;

public sealed record LinkTokenResult(string LinkToken, DateTimeOffset Expiration, string Kind);

public sealed class BankConnectionService
{
    public const string Bank = "bank";
    public const string Brokerage = "brokerage";

    private readonly PlaidContext _db;
    private readonly PlaidAccess _plaid;

    public BankConnectionService(PlaidContext db, PlaidAccess plaid)
    {
        _db = db;
        _plaid = plaid;
    }

    public static string Normalize(string userName) => userName.Trim().ToLowerInvariant();

    public static string? ParseKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return null;
        return kind.Trim().ToLowerInvariant() switch
        {
            Bank => Bank,
            Brokerage => Brokerage,
            _ => throw BankApiException.BadRequest("kind must be 'bank' or 'brokerage'."),
        };
    }

    public async Task<LinkTokenResult> CreateLinkTokenAsync(string userName, string? kindText, CancellationToken ct)
    {
        var kind = ParseKind(kindText) ?? Bank;
        var request = new LinkTokenCreateRequest
        {
            ClientName = "eShopOnWeb",
            Language = "en",
            CountryCodes = new[] { CountryCode.Us },
            User = new LinkTokenCreateRequestUser { ClientUserId = PlaidAccess.ClientUserId(userName) },
            // Paying needs transfer. Transactions are asked for as an additional consent so support can look at the
            // account's activity later without sending the shopper through Link again.
            Products = kind == Bank ? new[] { Products.Transfer } : new[] { Products.Investments },
            AdditionalConsentedProducts = kind == Bank ? new[] { Products.Transactions } : null,
        };

        try
        {
            var response = await _plaid.Client.LinkTokenCreate(new LinkTokenCreateOperationRequest { Body = request }, null, ct);
            return new LinkTokenResult(response.LinkToken, response.Expiration, kind);
        }
        catch (Exception e) when (PlaidFailure.ToApiException(e) is { } mapped) { throw mapped; }
    }

    public async Task<BankConnection> SaveAsync(string userName, string? publicToken, string? kindText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(publicToken))
            throw BankApiException.BadRequest("publicToken is required.");
        var explicitKind = ParseKind(kindText);
        var user = Normalize(userName);

        try
        {
            var exchange = await _plaid.Client.ItemPublicTokenExchange(new ItemPublicTokenExchangeOperationRequest
            {
                Body = new ItemPublicTokenExchangeRequest { PublicToken = publicToken.Trim() },
            }, null, ct);

            try
            {
            var item = (await _plaid.Client.ItemGet(new ItemGetOperationRequest
            {
                Body = new ItemGetRequest { AccessToken = exchange.AccessToken },
            }, null, ct)).Item;

            var accounts = (await _plaid.Client.AccountsGet(new AccountsGetOperationRequest
            {
                Body = new AccountsGetRequest { AccessToken = exchange.AccessToken },
            }, null, ct)).Accounts;

            var products = (item.Products ?? Array.Empty<Products>())
                .Concat(item.ConsentedProducts ?? Array.Empty<Products>())
                .Concat(item.BilledProducts ?? Array.Empty<Products>())
                .Select(p => p.Value).Distinct().ToList();
            var hasInvestments = products.Contains(Products.Investments.Value);
            var hasBankProducts = products.Any(p => p != Products.Investments.Value);

            var connection = await _db.Connections.Include(c => c.Accounts)
                .FirstOrDefaultAsync(c => c.UserName == user && c.ItemId == item.ItemId, ct);
            if (connection is null)
            {
                connection = new BankConnection { UserName = user, ItemId = item.ItemId };
                _db.Connections.Add(connection);
            }

            connection.ProtectedAccessToken = _plaid.Protect(exchange.AccessToken);
            connection.InstitutionId = item.InstitutionId;
            connection.InstitutionName = item.InstitutionName ?? item.InstitutionId ?? "Bank";
            connection.Products = string.Join(",", products);
            // The shopper's stated purpose wins; otherwise go by what the item was linked for.
            connection.IsBrokerage = explicitKind == Brokerage || (explicitKind is null && hasInvestments);
            connection.IsBank = explicitKind == Bank || (explicitKind is null && (hasBankProducts || !hasInvestments));

            connection.Accounts.Clear();
            foreach (var a in accounts)
            {
                connection.Accounts.Add(new BankAccount
                {
                    ConnectionId = connection.Id,
                    PlaidAccountId = a.AccountId,
                    Name = a.Name,
                    Mask = a.Mask,
                    Type = a.Type.Value,
                    Subtype = a.Subtype?.Value,
                });
            }

            await _db.SaveChangesAsync(ct);
            return connection;
            }
            catch
            {
                // Nothing was saved, so the item must not stay at Plaid where the shop would keep paying for it.
                await RemoveQuietlyAsync(exchange.AccessToken);
                throw;
            }
        }
        catch (Exception e) when (PlaidFailure.ToApiException(e) is { } mapped) { throw mapped; }
    }

    private async Task RemoveQuietlyAsync(string accessToken)
    {
        try
        {
            await _plaid.Client.ItemRemove(new ItemRemoveOperationRequest { Body = new ItemRemoveRequest { AccessToken = accessToken } });
        }
        catch (Exception)
        {
            // Best effort; the original failure is what the caller needs to hear about.
        }
    }

    public Task<List<BankConnection>> ListAsync(string userName, CancellationToken ct) =>
        _db.Connections.Include(c => c.Accounts).AsNoTracking()
            .Where(c => c.UserName == Normalize(userName)).OrderBy(c => c.CreatedAt).ToListAsync(ct);

    /// <summary>The shopper's own connection; someone else's looks exactly like a missing one.</summary>
    public async Task<BankConnection> GetOwnedAsync(string userName, Guid id, CancellationToken ct)
    {
        var user = Normalize(userName);
        var connection = await _db.Connections.Include(c => c.Accounts)
            .FirstOrDefaultAsync(c => c.Id == id && c.UserName == user, ct);
        return connection ?? throw BankApiException.NotFound("There is no such connected bank account.");
    }

    public async Task DisconnectAsync(string userName, Guid id, CancellationToken ct)
    {
        using var _ = await PlaidAccess.LockAsync(id, ct);
        var connection = await GetOwnedAsync(userName, id, ct);

        // Removing the item at Plaid is what stops the shop paying for it; only then forget it locally.
        try
        {
            await _plaid.Client.ItemRemove(new ItemRemoveOperationRequest
            {
                Body = new ItemRemoveRequest { AccessToken = _plaid.Unprotect(connection.ProtectedAccessToken) },
            }, null, ct);
        }
        catch (Exception e) when (PlaidFailure.HasCode(e, "ITEM_NOT_FOUND", "INVALID_ACCESS_TOKEN"))
        {
            // Plaid no longer has the item: nothing left to pay for.
        }
        catch (Exception e) when (PlaidFailure.ToApiException(e) is { } mapped) { throw mapped; }

        var copies = await _db.Transactions.Where(t => t.ConnectionId == id).ToListAsync(ct);
        _db.Transactions.RemoveRange(copies);
        _db.Connections.Remove(connection);
        await _db.SaveChangesAsync(ct);
    }
}
