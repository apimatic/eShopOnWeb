using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record LinkTokenResult(string LinkToken, DateTimeOffset Expiration);

public record BankConnectionDto(int Id, string Type, string? InstitutionId, string? InstitutionName, string? AccountName, string? AccountMask);

public class BankConnectionService(
    ThePlaidApiClient plaid,
    CatalogContext db,
    AccessTokenProtector protector,
    KeyedLocks locks)
{
    private const string ClientName = "eShopOnWeb";

    public static string NormalizeKind(string? kind) => (kind?.Trim().ToLowerInvariant()) switch
    {
        null or "" or "bank" => BankConnectionKind.Bank,
        "brokerage" => BankConnectionKind.Brokerage,
        _ => throw new RequestFailedException(400, "type must be 'bank' or 'brokerage'."),
    };

    /// <summary>What the browser needs to open Plaid Link for this shopper.</summary>
    public async Task<LinkTokenResult> CreateLinkTokenAsync(string userName, string? type)
    {
        var kind = NormalizeKind(type);

        var request = new LinkTokenCreateRequest
        {
            ClientName = ClientName,
            Language = "en",
            CountryCodes = [CountryCode.Us],
            User = new LinkTokenCreateRequestUser { ClientUserId = ClientUserId(userName) },
        };

        request = kind == BankConnectionKind.Bank
            ? request with
            {
                // Auth is what lets the account pay for orders; transactions power the support view and are
                // requested with a long history, as lookups reach back over any dates.
                Products = [Products.Auth],
                RequiredIfSupportedProducts = [Products.Transactions],
                Transactions = new LinkTokenTransactions { DaysRequested = 730 },
            }
            : request with { Products = [Products.Investments] };

        var response = await PlaidError.Call(() => plaid.LinkTokenCreate(new LinkTokenCreateOperationRequest { Body = request }));
        return new LinkTokenResult(response.LinkToken, response.Expiration);
    }

    /// <summary>Saves what Link handed the browser as a connection of this shopper.</summary>
    public async Task<BankConnectionDto> ConnectAsync(string userName, string? publicToken, string? type, string? accountId)
    {
        var kind = NormalizeKind(type);
        if (string.IsNullOrWhiteSpace(publicToken))
        {
            throw new RequestFailedException(400, "publicToken is required.");
        }

        var exchange = await PlaidError.Call(() => plaid.ItemPublicTokenExchange(new ItemPublicTokenExchangeOperationRequest
        {
            Body = new ItemPublicTokenExchangeRequest { PublicToken = publicToken.Trim() },
        }));

        // Already saved (a retried request): that Item is in use, so it must not be removed below.
        var existing = await db.BankConnections.FirstOrDefaultAsync(c => c.ItemId == exchange.ItemId);
        if (existing is not null)
        {
            if (existing.UserName != userName)
            {
                throw new RequestFailedException(409, "That bank account is already connected.");
            }

            return ToDto(existing);
        }

        try
        {
            var accounts = (await PlaidError.Call(() => plaid.AccountsGet(new AccountsGetOperationRequest
            {
                Body = new AccountsGetRequest { AccessToken = exchange.AccessToken },
            }))).Accounts;

            Account? chosen = null;
            if (kind == BankConnectionKind.Bank)
            {
                var payable = accounts.Where(IsPayable).ToList();
                chosen = string.IsNullOrWhiteSpace(accountId)
                    ? payable.OrderBy(a => a.Subtype == AccountSubtype.Checking ? 0 : 1).FirstOrDefault()
                    : payable.FirstOrDefault(a => a.AccountId == accountId);
                if (chosen is null)
                {
                    throw new RequestFailedException(422, string.IsNullOrWhiteSpace(accountId)
                        ? "That connection has no checking or savings account that can pay for an order."
                        : "accountId is not a checking or savings account of that connection.");
                }
            }
            else if (!accounts.Any(a => a.Type == AccountType.Investment))
            {
                throw new RequestFailedException(422, "That connection has no brokerage account.");
            }

            var (institutionId, institutionName) = await DescribeInstitutionAsync(exchange.AccessToken);

            var connection = new BankConnection(userName, kind, exchange.ItemId, protector.Protect(exchange.AccessToken),
                institutionId, institutionName, chosen?.AccountId, chosen?.Name, chosen?.Mask);
            db.BankConnections.Add(connection);
            await db.SaveChangesAsync();
            return ToDto(connection);
        }
        catch
        {
            // Plaid bills for every live Item; do not leave one behind that the shop will never use.
            await RemoveItemQuietlyAsync(exchange.AccessToken);
            throw;
        }
    }

    public async Task<BankConnectionDto> GetAsync(string userName, int connectionId) => ToDto(await FindAsync(userName, connectionId));

    /// <summary>The shopper's own connection; anyone else's looks like it does not exist.</summary>
    public async Task<BankConnection> FindAsync(string userName, int connectionId)
    {
        var connection = await db.BankConnections.FirstOrDefaultAsync(c => c.Id == connectionId && c.UserName == userName);
        return connection ?? throw new RequestFailedException(404, $"Connection {connectionId} was not found.");
    }

    /// <summary>
    /// Ends the connection at Plaid first, so the shop stops paying for the Item, and only then forgets it here.
    /// </summary>
    public async Task DisconnectAsync(string userName, int connectionId)
    {
        using var _ = await locks.AcquireAsync($"connection:{connectionId}");
        var connection = await FindAsync(userName, connectionId);
        var accessToken = protector.Unprotect(connection);

        try
        {
            await PlaidError.Call(() => plaid.ItemRemove(new ItemRemoveOperationRequest
            {
                Body = new ItemRemoveRequest { AccessToken = accessToken },
            }));
        }
        catch (RequestFailedException e) when (e.StatusCode == 404)
        {
            // Plaid no longer knows the Item, so there is nothing left to pay for.
        }

        var copies = db.BankTransactions.Where(t => t.ConnectionId == connection.Id);
        db.BankTransactions.RemoveRange(await copies.ToListAsync());
        db.BankConnections.Remove(connection);
        await db.SaveChangesAsync();
    }

    public static bool IsPayable(Account account) =>
        account.Type == AccountType.Depository && (account.Subtype == AccountSubtype.Checking || account.Subtype == AccountSubtype.Savings);

    private async Task<(string? Id, string? Name)> DescribeInstitutionAsync(string accessToken)
    {
        try
        {
            var item = (await plaid.ItemGet(new ItemGetOperationRequest { Body = new ItemGetRequest { AccessToken = accessToken } })).Item;
            var name = item.InstitutionName;
            if (name is null && item.InstitutionId is not null)
            {
                name = (await plaid.InstitutionsGetById(new InstitutionsGetByIdOperationRequest
                {
                    Body = new InstitutionsGetByIdRequest { InstitutionId = item.InstitutionId, CountryCodes = [CountryCode.Us] },
                })).Institution.Name;
            }

            return (item.InstitutionId, name);
        }
        catch (Exception e) when (PlaidError.Translate(e) is not null)
        {
            return (null, null); // only a label; the connection works without it
        }
    }

    private async Task RemoveItemQuietlyAsync(string accessToken)
    {
        try
        {
            await plaid.ItemRemove(new ItemRemoveOperationRequest { Body = new ItemRemoveRequest { AccessToken = accessToken } });
        }
        catch (Exception)
        {
            // best effort
        }
    }

    private static string ClientUserId(string userName) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userName.ToLowerInvariant()))).ToLowerInvariant();

    private static BankConnectionDto ToDto(BankConnection c) =>
        new(c.Id, c.Kind, c.InstitutionId, c.InstitutionName, c.AccountName, c.AccountMask);
}
