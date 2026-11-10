using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record LinkTokenResult(string LinkToken, DateTimeOffset Expiration);

public class ConnectionService
{
    private readonly ThePlaidApiClient _plaid;
    private readonly CatalogContext _db;
    private readonly IDataProtector _protector;
    private readonly KeyedLock _locks;
    private readonly ILogger<ConnectionService> _logger;

    public ConnectionService(ThePlaidApiClient plaid, CatalogContext db, IDataProtectionProvider dataProtection,
        KeyedLock locks, ILogger<ConnectionService> logger)
    {
        _plaid = plaid;
        _db = db;
        _protector = dataProtection.CreateProtector("eShopOnWeb.Plaid.AccessToken");
        _locks = locks;
        _logger = logger;
    }

    public static BankConnectionKind? ParseKind(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;
        return type.Trim().ToLowerInvariant() switch
        {
            "bank" => BankConnectionKind.Bank,
            "brokerage" => BankConnectionKind.Brokerage,
            _ => throw BankingException.BadRequest("type must be 'bank' or 'brokerage'."),
        };
    }

    // ---------------------------------------------------------------- link token

    public async Task<LinkTokenResult> CreateLinkTokenAsync(string shopperId, BankConnectionKind kind, CancellationToken ct)
    {
        var request = new LinkTokenCreateRequest
        {
            ClientName = "eShopOnWeb",
            Language = "en",
            CountryCodes = [CountryCode.Us],
            // Plaid asks for a stable id that is not personally identifiable.
            User = new LinkTokenCreateRequestUser { ClientUserId = ClientUserId(shopperId) },
        };

        request = kind == BankConnectionKind.Brokerage
            ? request with { Products = [Products.Investments] }
            : request with
            {
                // Pay by bank needs transfer; support looks at transactions, so ask for them up front
                // (with as much history as Plaid keeps) wherever the bank supports them.
                Products = [Products.Transfer],
                RequiredIfSupportedProducts = [Products.Transactions],
                Transactions = new LinkTokenTransactions { DaysRequested = 730 },
            };

        var response = await PlaidCalls.Run(() => _plaid.LinkTokenCreate(
            new LinkTokenCreateOperationRequest { Body = request }, cancellationToken: ct));
        return new LinkTokenResult(response.LinkToken, response.Expiration);
    }

    private static string ClientUserId(string shopperId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shopperId))).ToLowerInvariant();

    // ---------------------------------------------------------------- save

    public async Task<BankConnection> SaveAsync(string shopperId, string publicToken, BankConnectionKind? requestedKind,
        string? accountId, CancellationToken ct)
    {
        var exchange = await PlaidCalls.Run(() => _plaid.ItemPublicTokenExchange(new ItemPublicTokenExchangeOperationRequest
        {
            Body = new ItemPublicTokenExchangeRequest { PublicToken = publicToken }
        }, cancellationToken: ct));

        try
        {
            var item = (await PlaidCalls.Run(() => _plaid.ItemGet(new ItemGetOperationRequest
            {
                Body = new ItemGetRequest { AccessToken = exchange.AccessToken }
            }, cancellationToken: ct))).Item;

            var accounts = (await PlaidCalls.Run(() => _plaid.AccountsGet(new AccountsGetOperationRequest
            {
                Body = new AccountsGetRequest { AccessToken = exchange.AccessToken }
            }, cancellationToken: ct))).Accounts;

            var kind = requestedKind ?? InferKind(item);
            var connection = new BankConnection
            {
                OwnerId = shopperId,
                Kind = kind,
                ItemId = exchange.ItemId,
                ProtectedAccessToken = _protector.Protect(exchange.AccessToken),
                InstitutionId = item.InstitutionId,
                InstitutionName = item.InstitutionName,
            };

            if (kind == BankConnectionKind.Bank)
            {
                var account = PickPayingAccount(accounts, accountId);
                connection.AccountId = account.AccountId;
                connection.AccountName = account.Name;
                connection.AccountMask = account.Mask;
            }
            else if (!accounts.Any(a => a.Type == AccountType.Investment || a.Type == AccountType.Brokerage))
            {
                throw BankingException.BadRequest("That connection has no brokerage account.");
            }

            _db.BankConnections.Add(connection);
            await _db.SaveChangesAsync(ct);
            return connection;
        }
        catch
        {
            // Never leave an item behind that we are not tracking: the shop would keep paying Plaid for it.
            await TryRemoveItemAsync(exchange.AccessToken);
            throw;
        }
    }

    private static BankConnectionKind InferKind(ItemWithConsentFields item)
    {
        var products = item.BilledProducts.Concat(item.Products).Select(p => (string)p).ToHashSet();
        var onlyInvestments = products.Overlaps(new[] { "investments", "investments_auth" })
                              && !products.Overlaps(new[] { "auth", "transfer", "transactions", "balance" });
        return onlyInvestments ? BankConnectionKind.Brokerage : BankConnectionKind.Bank;
    }

    private static Account PickPayingAccount(IReadOnlyList<Account> accounts, string? accountId)
    {
        var depository = accounts.Where(a => a.Type == AccountType.Depository).ToList();
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            return depository.FirstOrDefault(a => a.AccountId == accountId)
                   ?? throw BankingException.BadRequest("accountId is not a checking or savings account of that connection.");
        }

        return depository.FirstOrDefault(a => (string?)a.Subtype == "checking")
               ?? depository.FirstOrDefault(a => (string?)a.Subtype == "savings")
               ?? depository.FirstOrDefault()
               ?? throw BankingException.BadRequest("That connection has no account the shop can be paid from.");
    }

    private async Task TryRemoveItemAsync(string accessToken)
    {
        try
        {
            await _plaid.ItemRemove(new ItemRemoveOperationRequest { Body = new ItemRemoveRequest { AccessToken = accessToken } });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove a Plaid item that was not saved.");
        }
    }

    // ---------------------------------------------------------------- disconnect

    public async Task DisconnectAsync(string shopperId, int connectionId, CancellationToken ct)
    {
        using var _ = await _locks.AcquireAsync($"connection:{connectionId}", ct);

        var connection = await FindOwnedAsync(shopperId, connectionId, ct);

        if (connection.ProtectedAccessToken is not null)
        {
            string accessToken;
            try
            {
                accessToken = _protector.Unprotect(connection.ProtectedAccessToken);
            }
            catch (CryptographicException)
            {
                throw BankingException.Conflict("The stored bank credentials cannot be read any more.");
            }

            try
            {
                // Removing the item is what stops Plaid billing the shop for it.
                await _plaid.ItemRemove(new ItemRemoveOperationRequest
                {
                    Body = new ItemRemoveRequest { AccessToken = accessToken }
                }, cancellationToken: ct);
            }
            catch (Exception ex) when (PlaidCalls.IsPlaidError(ex, "ITEM_NOT_FOUND") || PlaidCalls.IsPlaidError(ex, "INVALID_ACCESS_TOKEN"))
            {
                // Plaid no longer knows the item: nothing left to pay for.
            }
            catch (ThePlaidApi.Core.Exceptions.ApiException<ThePlaidApi.Core.ErrorResponse.RawError> ex)
            {
                throw PlaidErrorTranslator.Translate(ex);
            }
        }

        connection.IsActive = false;
        connection.ProtectedAccessToken = null;
        connection.SyncCursor = null;
        connection.DisconnectedAt = DateTimeOffset.UtcNow;
        _db.BankTransactions.RemoveRange(_db.BankTransactions.Where(t => t.ConnectionId == connection.Id));
        await _db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- lookups

    /// <summary>A connection of this shopper that is still connected; 404 for anyone else's or a removed one.</summary>
    public async Task<BankConnection> FindOwnedAsync(string shopperId, int connectionId, CancellationToken ct)
    {
        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == connectionId, ct);
        if (connection is null || connection.OwnerId != shopperId || !connection.IsActive)
            throw BankingException.NotFound("No such connected bank.");
        return connection;
    }

    public Task<List<BankConnection>> ListActiveAsync(string shopperId, BankConnectionKind kind, CancellationToken ct) =>
        _db.BankConnections.Where(c => c.OwnerId == shopperId && c.IsActive && c.Kind == kind)
            .OrderBy(c => c.Id).ToListAsync(ct);

    public string AccessTokenOf(BankConnection connection)
    {
        if (!connection.IsActive || connection.ProtectedAccessToken is null)
            throw BankingException.Conflict("That bank is no longer connected.");
        try
        {
            return _protector.Unprotect(connection.ProtectedAccessToken);
        }
        catch (CryptographicException)
        {
            throw BankingException.Conflict("The stored bank credentials cannot be read any more; connect the bank again.");
        }
    }
}
