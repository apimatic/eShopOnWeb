using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.PayByBank;

public record LinkTokenDto(string LinkToken, DateTimeOffset Expiration);

public record ConnectionDto(
    int ConnectionId,
    string Kind,
    string? InstitutionName,
    string? AccountId,
    string? AccountName,
    string? AccountMask);

/// <summary>Protects Plaid access tokens at rest.</summary>
public class BankTokenProtector
{
    private readonly IDataProtector _protector;

    public BankTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("PayByBank.PlaidAccessToken.v1");
    }

    public string Protect(string accessToken) => _protector.Protect(accessToken);

    public string Unprotect(BankConnection connection)
    {
        try
        {
            return _protector.Unprotect(connection.ProtectedAccessToken);
        }
        catch (CryptographicException ex)
        {
            throw new PayByBankException(HttpStatusCode.Conflict,
                "The stored bank credentials can no longer be read; reconnect the bank.", ex);
        }
    }
}

public class BankConnectionService
{
    private readonly CatalogContext _db;
    private readonly PlaidGateway _plaid;
    private readonly BankTokenProtector _tokens;

    public BankConnectionService(CatalogContext db, PlaidGateway plaid, BankTokenProtector tokens)
    {
        _db = db;
        _plaid = plaid;
        _tokens = tokens;
    }

    public static BankConnectionKind ParseKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        null or "" or "bank" => BankConnectionKind.Bank,
        "brokerage" => BankConnectionKind.Brokerage,
        _ => throw Fail.BadRequest("kind must be 'bank' or 'brokerage'.")
    };

    public static string KindName(BankConnectionKind kind) => kind == BankConnectionKind.Bank ? "bank" : "brokerage";

    public async Task<LinkTokenDto> CreateLinkTokenAsync(string userId, string? kindText, string? institutionId)
    {
        var kind = ParseKind(kindText);
        var request = new LinkTokenCreateRequest
        {
            ClientName = "eShopOnWeb",
            Language = "en",
            CountryCodes = [CountryCode.Us],
            // Plaid asks for an opaque id, not an email address.
            User = new LinkTokenCreateRequestUser { ClientUserId = OpaqueUserId(userId) },
            InstitutionId = string.IsNullOrWhiteSpace(institutionId) ? null : institutionId.Trim(),
            Products = kind == BankConnectionKind.Bank ? [Products.Auth] : [Products.Investments],
            // Activity and look-ups need transactions; ask for them wherever the bank offers them.
            RequiredIfSupportedProducts = kind == BankConnectionKind.Bank ? [Products.Transactions] : null,
            Transactions = kind == BankConnectionKind.Bank ? new LinkTokenTransactions { DaysRequested = 730 } : null,
            AccountFilters = kind == BankConnectionKind.Bank
                ? new LinkTokenAccountFilters
                {
                    Depository = new DepositoryFilter { AccountSubtypes = [DepositoryAccountSubtype.Checking, DepositoryAccountSubtype.Savings] }
                }
                : null
        };
        var response = await _plaid.Call(c => c.LinkTokenCreate(new LinkTokenCreateOperationRequest { Body = request }));
        return new LinkTokenDto(response.LinkToken, response.Expiration);
    }

    public async Task<ConnectionDto> ConnectAsync(string userId, string? publicToken, string? kindText, string? accountId)
    {
        if (string.IsNullOrWhiteSpace(publicToken)) throw Fail.BadRequest("publicToken is required.");
        var requestedKind = kindText == null ? (BankConnectionKind?)null : ParseKind(kindText);

        var exchange = await _plaid.Call(c => c.ItemPublicTokenExchange(new ItemPublicTokenExchangeOperationRequest
        {
            Body = new ItemPublicTokenExchangeRequest { PublicToken = publicToken.Trim() }
        }));

        // From here on Plaid bills for the Item: never leave one behind that we do not keep.
        try
        {
            var accounts = await _plaid.Call(c => c.AccountsGet(new AccountsGetOperationRequest
            {
                Body = new AccountsGetRequest { AccessToken = exchange.AccessToken }
            }));

            var banks = accounts.Accounts.Where(IsPayable).ToList();
            var investments = accounts.Accounts.Where(a => a.Type == AccountType.Investment || a.Type == AccountType.Brokerage).ToList();
            var kind = requestedKind ?? (banks.Count > 0 ? BankConnectionKind.Bank : BankConnectionKind.Brokerage);

            Account? chosen = null;
            if (kind == BankConnectionKind.Bank)
            {
                if (banks.Count == 0)
                    throw Fail.BadRequest("No checking or savings account was shared, so this connection cannot pay for orders.");
                if (accountId != null)
                {
                    chosen = banks.FirstOrDefault(a => a.AccountId == accountId)
                             ?? throw Fail.BadRequest("accountId is not a checking or savings account of this connection.");
                }
                else
                {
                    chosen = banks.FirstOrDefault(a => a.Subtype == AccountSubtype.Checking) ?? banks[0];
                }
            }
            else if (investments.Count == 0)
            {
                throw Fail.BadRequest("No brokerage account was shared, so there is no portfolio to show.");
            }

            var institutionId = accounts.Item.InstitutionId;
            var institutionName = accounts.Item.InstitutionName ?? await TryGetInstitutionNameAsync(institutionId);

            var connection = new BankConnection
            {
                UserId = userId,
                Kind = kind,
                PlaidItemId = exchange.ItemId,
                ProtectedAccessToken = _tokens.Protect(exchange.AccessToken),
                AccountId = chosen?.AccountId,
                AccountName = chosen?.Name,
                AccountMask = chosen?.Mask,
                InstitutionId = institutionId,
                InstitutionName = institutionName
            };
            _db.BankConnections.Add(connection);
            await _db.SaveChangesAsync();
            return ToDto(connection);
        }
        catch
        {
            await TryRemoveItemAsync(exchange.AccessToken);
            throw;
        }
    }

    public async Task DisconnectAsync(string userId, int connectionId)
    {
        var connection = await GetOwnedAsync(userId, connectionId);
        using var _ = await KeyedLock.EnterAsync("connection:" + connectionId);

        // Billing stops only once Plaid has removed the Item, so do that first and keep our row if it fails.
        try
        {
            var accessToken = _tokens.Unprotect(connection);
            await _plaid.Call(c => c.ItemRemove(new ItemRemoveOperationRequest
            {
                Body = new ItemRemoveRequest { AccessToken = accessToken }
            }));
        }
        catch (PlaidApiException ex) when (ex.ErrorCode is "ITEM_NOT_FOUND" or "INVALID_ACCESS_TOKEN")
        {
            // Plaid already dropped it; nothing left to pay for.
        }

        var transactions = await _db.BankTransactions.Where(t => t.ConnectionId == connectionId).ToListAsync();
        _db.BankTransactions.RemoveRange(transactions);
        var openCheckouts = await _db.BankCheckouts
            .Where(c => c.ConnectionId == connectionId && c.Status == BankCheckoutStatus.Open).ToListAsync();
        _db.BankCheckouts.RemoveRange(openCheckouts);
        _db.BankConnections.Remove(connection);
        await _db.SaveChangesAsync();
    }

    public async Task<BankConnection> GetOwnedAsync(string userId, int connectionId)
    {
        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == connectionId && c.UserId == userId);
        return connection ?? throw Fail.NotFound("No such bank connection for this shopper.");
    }

    public static ConnectionDto ToDto(BankConnection c) =>
        new(c.Id, KindName(c.Kind), c.InstitutionName, c.AccountId, c.AccountName, c.AccountMask);

    private static bool IsPayable(Account a) =>
        a.Type == AccountType.Depository && (a.Subtype == AccountSubtype.Checking || a.Subtype == AccountSubtype.Savings);

    private async Task<string?> TryGetInstitutionNameAsync(string? institutionId)
    {
        if (string.IsNullOrEmpty(institutionId)) return null;
        try
        {
            var r = await _plaid.Call(c => c.InstitutionsGetById(new InstitutionsGetByIdOperationRequest
            {
                Body = new InstitutionsGetByIdRequest { InstitutionId = institutionId, CountryCodes = [CountryCode.Us] }
            }));
            return r.Institution.Name;
        }
        catch (PayByBankException)
        {
            return null;
        }
    }

    private async Task TryRemoveItemAsync(string accessToken)
    {
        try
        {
            await _plaid.Call(c => c.ItemRemove(new ItemRemoveOperationRequest
            {
                Body = new ItemRemoveRequest { AccessToken = accessToken }
            }));
        }
        catch (PayByBankException)
        {
            // Best effort: the original failure is what the caller needs to see.
        }
    }

    private static string OpaqueUserId(string userId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId))).ToLowerInvariant();
}
