using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record LinkTokenInfo(string LinkToken, DateTimeOffset Expiration);

public record BankConnectionInfo(
    int Id,
    string Kind,
    string? InstitutionId,
    string? InstitutionName,
    string? AccountName,
    string? AccountMask,
    DateTimeOffset CreatedAt);

/// <summary>
/// Connecting and disconnecting bank and brokerage accounts.
/// </summary>
public class BankConnectionService
{
    public const string PURPOSE_BANK = BankConnection.KIND_BANK;
    public const string PURPOSE_BROKERAGE = BankConnection.KIND_BROKERAGE;

    private readonly ThePlaidApiClient _plaid;
    private readonly CatalogContext _db;
    private readonly AccessTokenProtector _protector;
    private readonly ConnectionLocks _locks;

    public BankConnectionService(ThePlaidApiClient plaid, CatalogContext db, AccessTokenProtector protector, ConnectionLocks locks)
    {
        _plaid = plaid;
        _db = db;
        _protector = protector;
        _locks = locks;
    }

    public static string NormalizeBuyer(string buyerId) => buyerId.Trim().ToLowerInvariant();

    /// <summary>
    /// Plaid refuses personally identifying values (such as an email) as the user id, so give it a stable opaque one.
    /// </summary>
    private static string ClientUserId(string buyerId) =>
        "eshop-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(NormalizeBuyer(buyerId)))).ToLowerInvariant().Substring(0, 32);

    public static BankConnectionInfo ToInfo(BankConnection c) =>
        new(c.Id, c.Kind, c.InstitutionId, c.InstitutionName, c.AccountName, c.AccountMask, c.CreatedAt);

    public async Task<LinkTokenInfo> CreateLinkTokenAsync(string buyerId, string? purpose, CancellationToken ct)
    {
        var kind = ParsePurpose(purpose) ?? PURPOSE_BANK;
        var request = new LinkTokenCreateRequest
        {
            ClientName = "eShopOnWeb",
            Language = "en",
            CountryCodes = new[] { CountryCode.Us },
            User = new LinkTokenCreateRequestUser { ClientUserId = ClientUserId(buyerId) },
            Products = kind == PURPOSE_BROKERAGE ? new[] { Products.Investments } : new[] { Products.Transfer },
            // Support staff look up a connected bank's transactions; ask for that consent up front.
            AdditionalConsentedProducts = kind == PURPOSE_BROKERAGE ? null : new[] { Products.Transactions },
        };

        var response = await PlaidCall.Run(() => _plaid.LinkTokenCreate(
            new LinkTokenCreateOperationRequest { Body = request }, cancellationToken: ct));
        return new LinkTokenInfo(response.LinkToken, response.Expiration);
    }

    public async Task<BankConnectionInfo> AddConnectionAsync(
        string buyerId, string publicToken, string? accountId, string? purpose, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(publicToken)) throw new BankingException(400, "publicToken is required.");
        var requestedKind = ParsePurpose(purpose);
        if (purpose is not null && requestedKind is null)
        {
            throw new BankingException(400, "purpose must be 'bank' or 'brokerage'.");
        }

        var exchange = await PlaidCall.Run(() => _plaid.ItemPublicTokenExchange(
            new ItemPublicTokenExchangeOperationRequest
            {
                Body = new ItemPublicTokenExchangeRequest { PublicToken = publicToken.Trim() },
            }, cancellationToken: ct));

        var accessToken = exchange.AccessToken;
        try
        {
            var accounts = await PlaidCall.Run(() => _plaid.AccountsGet(
                new AccountsGetOperationRequest { Body = new AccountsGetRequest { AccessToken = accessToken } },
                cancellationToken: ct));

            var depository = accounts.Accounts.Where(a => a.Type == AccountType.Depository).ToList();
            var investments = accounts.Accounts.Where(a => a.Type == AccountType.Investment).ToList();

            var kind = requestedKind ?? InferKind(accounts.Item, depository.Count, investments.Count);

            Account? payAccount = null;
            if (kind == PURPOSE_BANK)
            {
                payAccount = ChoosePayAccount(depository, accountId);
            }
            else if (investments.Count == 0)
            {
                throw new BankingException(422, "The connection has no brokerage account.");
            }

            var connection = new BankConnection
            {
                BuyerId = NormalizeBuyer(buyerId),
                ItemId = exchange.ItemId,
                ProtectedAccessToken = _protector.Protect(accessToken),
                InstitutionId = accounts.Item.InstitutionId,
                InstitutionName = accounts.Item.InstitutionName ?? await TryGetInstitutionNameAsync(accounts.Item.InstitutionId, ct),
                Kind = kind,
                AccountId = payAccount?.AccountId,
                AccountName = payAccount?.Name,
                AccountMask = payAccount?.Mask,
                HasDepositoryAccounts = depository.Count > 0,
                HasInvestmentAccounts = investments.Count > 0,
            };
            _db.BankConnections.Add(connection);
            await _db.SaveChangesAsync(ct);
            return ToInfo(connection);
        }
        catch
        {
            // Don't leave an item behind that the shop is billed for but can't use.
            await TryRemoveItemAsync(accessToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<BankConnectionInfo>> ListAsync(string buyerId, CancellationToken ct)
    {
        var buyer = NormalizeBuyer(buyerId);
        var items = await _db.BankConnections.AsNoTracking().Where(c => c.BuyerId == buyer).OrderBy(c => c.Id).ToListAsync(ct);
        return items.Select(ToInfo).ToList();
    }

    public async Task RemoveAsync(string buyerId, int connectionId, CancellationToken ct)
    {
        var buyer = NormalizeBuyer(buyerId);
        using var _ = await _locks.AcquireAsync($"connection-{connectionId}", ct);

        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == connectionId && c.BuyerId == buyer, ct)
            ?? throw new BankingException(404, "No such bank connection.");

        var accessToken = _protector.Unprotect(connection);
        try
        {
            await PlaidCall.Run(() => _plaid.ItemRemove(
                new ItemRemoveOperationRequest { Body = new ItemRemoveRequest { AccessToken = accessToken } },
                cancellationToken: ct));
        }
        catch (PlaidCallException ex) when (ex.ErrorCode is "ITEM_NOT_FOUND" or "INVALID_ACCESS_TOKEN")
        {
            // Plaid no longer has the item: nothing left to stop paying for.
        }

        var transactions = _db.BankTransactions.Where(t => t.ConnectionId == connection.Id);
        _db.BankTransactions.RemoveRange(await transactions.ToListAsync(ct));
        _db.BankConnections.Remove(connection);
        await _db.SaveChangesAsync(ct);
    }

    private static string? ParsePurpose(string? purpose) => purpose?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "bank" => PURPOSE_BANK,
        "brokerage" or "portfolio" => PURPOSE_BROKERAGE,
        _ => null,
    };

    private static string InferKind(Item item, int depositoryCount, int investmentCount)
    {
        var products = item.BilledProducts.Concat(item.Products ?? Array.Empty<Products>()).ToList();
        var investmentsOnly = products.Contains(Products.Investments)
            && !products.Any(p => p == Products.Transfer || p == Products.Auth || p == Products.Transactions);
        if (investmentCount > 0 && (depositoryCount == 0 || investmentsOnly)) return PURPOSE_BROKERAGE;
        return PURPOSE_BANK;
    }

    private static Account ChoosePayAccount(List<Account> depository, string? accountId)
    {
        if (depository.Count == 0)
        {
            throw new BankingException(422, "The connection has no checking or savings account to pay with.");
        }

        if (!string.IsNullOrWhiteSpace(accountId))
        {
            return depository.FirstOrDefault(a => a.AccountId == accountId.Trim())
                ?? throw new BankingException(400, "accountId is not a checking or savings account of this connection.");
        }

        return depository.FirstOrDefault(a => a.Subtype == AccountSubtype.Checking)
            ?? depository.FirstOrDefault(a => a.Subtype == AccountSubtype.Savings)
            ?? depository[0];
    }

    private async Task<string?> TryGetInstitutionNameAsync(string? institutionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(institutionId)) return null;
        try
        {
            var response = await PlaidCall.Run(() => _plaid.InstitutionsGetById(new InstitutionsGetByIdOperationRequest
            {
                Body = new InstitutionsGetByIdRequest { InstitutionId = institutionId, CountryCodes = new[] { CountryCode.Us } },
            }, cancellationToken: ct));
            return response.Institution.Name;
        }
        catch (BankingException)
        {
            return null;
        }
    }

    private async Task TryRemoveItemAsync(string accessToken)
    {
        try
        {
            await PlaidCall.Run(() => _plaid.ItemRemove(
                new ItemRemoveOperationRequest { Body = new ItemRemoveRequest { AccessToken = accessToken } }));
        }
        catch (BankingException)
        {
            // Best effort.
        }
    }
}
