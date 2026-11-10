using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public record LinkTokenDto(string LinkToken, DateTimeOffset Expiration);

public record AccountDto(string Id, string Name, string? Mask, string Type, string? Subtype);

public record ConnectionDto(
    int ConnectionId,
    string Kind,
    string? Institution,
    string? PaymentAccountId,
    IReadOnlyList<AccountDto> Accounts);

public class BankConnectionService
{
    private const string ClientName = "eShopOnWeb";

    private readonly ThePlaidApiClient _plaid;
    private readonly CatalogContext _db;
    private readonly AccessTokenProtector _protector;
    private readonly InstitutionService _institutions;

    public BankConnectionService(ThePlaidApiClient plaid, CatalogContext db, AccessTokenProtector protector,
        InstitutionService institutions)
    {
        _plaid = plaid;
        _db = db;
        _protector = protector;
        _institutions = institutions;
    }

    public static BankConnectionKind ParseKind(string? kind) =>
        string.IsNullOrWhiteSpace(kind) || kind.Trim().Equals("bank", StringComparison.OrdinalIgnoreCase)
            ? BankConnectionKind.Bank
            : kind.Trim().Equals("brokerage", StringComparison.OrdinalIgnoreCase)
                ? BankConnectionKind.Brokerage
                : throw ApiProblemException.BadRequest("kind must be 'bank' or 'brokerage'.");

    public async Task<LinkTokenDto> CreateLinkTokenAsync(string userName, string? kind)
    {
        var parsedKind = ParseKind(kind);
        var request = new LinkTokenCreateRequest
        {
            ClientName = ClientName,
            Language = "en",
            CountryCodes = [CountryCode.Us],
            // Plaid asks for an opaque identifier, not the shopper's email.
            User = new LinkTokenCreateRequestUser { ClientUserId = ClientUserId(userName) },
            Products = parsedKind == BankConnectionKind.Bank ? [Products.Auth] : [Products.Investments],
            RequiredIfSupportedProducts = parsedKind == BankConnectionKind.Bank ? [Products.Transactions] : null
        };

        var response = await PlaidCall.RunAsync(() => _plaid.LinkTokenCreate(new() { Body = request })).ConfigureAwait(false);
        return new LinkTokenDto(response.LinkToken, response.Expiration);
    }

    public async Task<ConnectionDto> ConnectAsync(string userName, string? publicToken, string? kind, string? accountId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publicToken))
            throw ApiProblemException.BadRequest("publicToken is required.");
        var requestedKind = string.IsNullOrWhiteSpace(kind) ? (BankConnectionKind?)null : ParseKind(kind);

        var exchange = await PlaidCall.RunAsync(() => _plaid.ItemPublicTokenExchange(new()
        {
            Body = new() { PublicToken = publicToken.Trim() }
        })).ConfigureAwait(false);

        try
        {
            var accounts = await PlaidCall.RunAsync(() => _plaid.AccountsGet(new()
            {
                Body = new() { AccessToken = exchange.AccessToken }
            })).ConfigureAwait(false);

            var bankAccounts = accounts.Accounts.Where(a => a.Type == AccountType.Depository).ToList();
            var brokerageAccounts = accounts.Accounts
                .Where(a => a.Type == AccountType.Investment || a.Type == AccountType.Brokerage).ToList();

            var resolvedKind = requestedKind ??
                               (bankAccounts.Count > 0 ? BankConnectionKind.Bank : BankConnectionKind.Brokerage);

            Account? paymentAccount = null;
            if (resolvedKind == BankConnectionKind.Bank)
            {
                if (bankAccounts.Count == 0)
                    throw ApiProblemException.Unprocessable("This login has no bank (checking or savings) account to pay with.");
                if (!string.IsNullOrWhiteSpace(accountId))
                {
                    paymentAccount = bankAccounts.FirstOrDefault(a => a.AccountId == accountId.Trim())
                                     ?? throw ApiProblemException.Unprocessable("accountId is not a bank account of this login.");
                }
                else
                {
                    paymentAccount = bankAccounts.FirstOrDefault(a => a.Subtype == AccountSubtype.Checking) ?? bankAccounts[0];
                }
            }
            else if (brokerageAccounts.Count == 0)
            {
                throw ApiProblemException.Unprocessable("This login has no brokerage account.");
            }

            var userKey = CurrentUser.Key(userName);
            var institutionId = accounts.Item.InstitutionId;
            var institutionName = accounts.Item.InstitutionName;
            if (institutionName == null && institutionId != null)
                institutionName = await _institutions.GetNameAsync(institutionId).ConfigureAwait(false);

            var existing = await _db.BankConnections
                .FirstOrDefaultAsync(c => c.PlaidItemId == exchange.ItemId, cancellationToken).ConfigureAwait(false);
            if (existing != null && existing.UserKey != userKey)
                throw ApiProblemException.Conflict("This bank login is already connected to another shopper.");

            var connection = existing ?? new BankConnection { UserName = userName, UserKey = userKey, PlaidItemId = exchange.ItemId };
            connection.Kind = resolvedKind;
            connection.ProtectedAccessToken = _protector.Protect(exchange.AccessToken);
            connection.InstitutionId = institutionId;
            connection.InstitutionName = institutionName;
            connection.PaymentAccountId = paymentAccount?.AccountId;
            connection.PaymentAccountName = paymentAccount?.Name;
            connection.PaymentAccountMask = paymentAccount?.Mask;
            if (existing == null)
                _db.BankConnections.Add(connection);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var shown = resolvedKind == BankConnectionKind.Bank ? bankAccounts : brokerageAccounts;
            return new ConnectionDto(connection.Id, KindName(resolvedKind), institutionName, connection.PaymentAccountId,
                shown.Select(a => new AccountDto(a.AccountId, a.Name, a.Mask, a.Type.Value, a.Subtype?.Value)).ToList());
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Never leave an Item behind that we aren't tracking: the shop would keep paying Plaid for it.
            await TryRemoveItemAsync(exchange.ItemId, exchange.AccessToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisconnectAsync(string userName, int connectionId, CancellationToken cancellationToken)
    {
        var connection = await GetOwnedAsync(userName, connectionId, cancellationToken).ConfigureAwait(false);
        var accessToken = _protector.Unprotect(connection.ProtectedAccessToken);

        try
        {
            await PlaidCall.RunAsync(() => _plaid.ItemRemove(new()
            {
                Body = new() { AccessToken = accessToken }
            })).ConfigureAwait(false);
        }
        catch (Exception ex) when (PlaidCall.IsCode(ex, "ITEM_NOT_FOUND"))
        {
            // Plaid no longer has the Item, so there is nothing left to pay for.
        }

        var transactions = await _db.BankTransactions.Where(t => t.BankConnectionId == connection.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        _db.BankTransactions.RemoveRange(transactions);
        _db.BankConnections.Remove(connection);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The caller's own connection, or a 404: other shoppers' connections are not revealed.</summary>
    public async Task<BankConnection> GetOwnedAsync(string userName, int connectionId, CancellationToken cancellationToken)
    {
        var userKey = CurrentUser.Key(userName);
        var connection = await _db.BankConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.UserKey == userKey, cancellationToken).ConfigureAwait(false);
        return connection ?? throw ApiProblemException.NotFound("There is no such connected bank or brokerage account.");
    }

    private async Task TryRemoveItemAsync(string itemId, string accessToken)
    {
        try
        {
            await PlaidCall.RunAsync(() => _plaid.ItemRemove(new() { Body = new() { AccessToken = accessToken } }))
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // best effort - the original failure is the one to report
        }
    }

    public static string KindName(BankConnectionKind kind) => kind == BankConnectionKind.Bank ? "bank" : "brokerage";

    private static string ClientUserId(string userName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(CurrentUser.Key(userName)));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
