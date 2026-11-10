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
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public sealed record LinkTokenDto(string LinkToken, DateTimeOffset Expiration, string Kind);

public sealed record BankConnectionDto(
    string ConnectionId,
    string Kind,
    string? InstitutionId,
    string? InstitutionName,
    string? AccountName,
    string? AccountMask,
    DateTimeOffset ConnectedAt);

/// <summary>Connecting and disconnecting the bank and brokerage accounts of a shopper.</summary>
public class BankConnectionService
{
    public const string BankKind = "bank";
    public const string BrokerageKind = "brokerage";

    private readonly PlaidApi _plaid;
    private readonly CatalogContext _db;
    private readonly IDataProtector _protector;
    private readonly ILogger<BankConnectionService> _logger;

    public BankConnectionService(PlaidApi plaid, CatalogContext db, IDataProtectionProvider dataProtection,
        ILogger<BankConnectionService> logger)
    {
        _plaid = plaid;
        _db = db;
        _protector = dataProtection.CreateProtector("eShopOnWeb.Plaid.AccessToken");
        _logger = logger;
    }

    public static string ParseKind(string? kind) => (kind?.Trim().ToLowerInvariant()) switch
    {
        null or "" or BankKind => BankKind,
        BrokerageKind => BrokerageKind,
        _ => throw new ApiRequestException(HttpStatusCode.BadRequest, "kind must be 'bank' or 'brokerage'.")
    };

    /// <summary>Plaid's user id for a shopper: it must not be an email address.</summary>
    public static string PlaidUserId(string userName) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToLowerInvariant()))).ToLowerInvariant();

    public string RevealAccessToken(BankConnection connection)
    {
        try
        {
            return _protector.Unprotect(connection.ProtectedAccessToken);
        }
        catch (CryptographicException ex)
        {
            throw new ApiRequestException(HttpStatusCode.Conflict,
                "The saved connection can't be used any more, connect the bank again.", ex);
        }
    }

    public async Task<LinkTokenDto> CreateLinkTokenAsync(string userName, string? kind)
    {
        kind = ParseKind(kind);
        var request = new LinkTokenCreateRequest
        {
            ClientName = "eShopOnWeb",
            Language = "en",
            CountryCodes = new[] { CountryCode.Us },
            User = new LinkTokenCreateRequestUser { ClientUserId = PlaidUserId(userName) },
        };

        request = kind == BrokerageKind
            ? request with { Products = new[] { Products.Investments } }
            // Banks pay with Transfer; Transactions is consented up front so support can look up the activity later.
            : request with
            {
                Products = new[] { Products.Transfer },
                AdditionalConsentedProducts = new[] { Products.Transactions }
            };

        var response = await _plaid.CallAsync(c => c.LinkTokenCreate(new LinkTokenCreateOperationRequest { Body = request }));
        return new LinkTokenDto(response.LinkToken, response.Expiration, kind);
    }

    public async Task<IReadOnlyList<BankConnectionDto>> ListAsync(string userName)
    {
        var connections = await _db.BankConnections.AsNoTracking()
            .Where(c => c.UserName == userName).OrderBy(c => c.Id).ToListAsync();
        return connections.Select(ToDto).ToList();
    }

    public async Task<BankConnectionDto> ConnectAsync(string userName, string? publicToken, string? kind)
    {
        if (string.IsNullOrWhiteSpace(publicToken))
        {
            throw new ApiRequestException(HttpStatusCode.BadRequest, "publicToken is required.");
        }

        var requestedKind = string.IsNullOrWhiteSpace(kind) ? null : ParseKind(kind);

        var exchange = await _plaid.CallAsync(c => c.ItemPublicTokenExchange(new ItemPublicTokenExchangeOperationRequest
        {
            Body = new ItemPublicTokenExchangeRequest { PublicToken = publicToken!.Trim() }
        }));
        var accessToken = exchange.AccessToken;

        try
        {
            var existing = await _db.BankConnections.FirstOrDefaultAsync(c => c.ItemId == exchange.ItemId);
            if (existing is not null)
            {
                if (existing.UserName != userName)
                {
                    throw new ApiRequestException(HttpStatusCode.Conflict, "This account is already connected by another shopper.");
                }

                // Same Item again (e.g. a repeated request): keep the single connection, with the fresh token.
                existing.ProtectedAccessToken = _protector.Protect(accessToken);
                await _db.SaveChangesAsync();
                return ToDto(existing);
            }

            var item = (await _plaid.CallAsync(c => c.ItemGet(new ItemGetOperationRequest
            {
                Body = new ItemGetRequest { AccessToken = accessToken }
            }))).Item;

            var itemProducts = (item.Products ?? Array.Empty<Products>())
                .Concat(item.BilledProducts).Select(p => p.Value).ToHashSet();
            var isBrokerage = itemProducts.Contains(Products.Investments.Value) &&
                              !itemProducts.Contains(Products.Transfer.Value) &&
                              !itemProducts.Contains(Products.Auth.Value) &&
                              !itemProducts.Contains(Products.Transactions.Value);
            var actualKind = isBrokerage ? BrokerageKind : BankKind;
            if (requestedKind is not null && requestedKind != actualKind)
            {
                throw new ApiRequestException(HttpStatusCode.UnprocessableEntity,
                    $"The connected account is a {actualKind} account, not a {requestedKind} account.");
            }

            var connection = new BankConnection
            {
                UserName = userName,
                ItemId = exchange.ItemId,
                ProtectedAccessToken = _protector.Protect(accessToken),
                InstitutionId = item.InstitutionId,
                InstitutionName = item.InstitutionName,
                Kind = isBrokerage ? BankConnectionKind.Brokerage : BankConnectionKind.Bank
            };

            if (!isBrokerage)
            {
                var accounts = (await _plaid.CallAsync(c => c.AccountsGet(new AccountsGetOperationRequest
                {
                    Body = new AccountsGetRequest { AccessToken = accessToken }
                }))).Accounts;

                var payment = accounts.FirstOrDefault(a => a.Type.Value == "depository" && a.Subtype?.Value == "checking")
                              ?? accounts.FirstOrDefault(a => a.Type.Value == "depository" && a.Subtype?.Value == "savings");
                if (payment is not null)
                {
                    connection.PaymentAccountId = payment.AccountId;
                    connection.PaymentAccountName = payment.Name;
                    connection.PaymentAccountMask = payment.Mask;
                }
            }

            _db.BankConnections.Add(connection);
            await _db.SaveChangesAsync();
            return ToDto(connection);
        }
        catch
        {
            // Never leave an Item the shop can't reach: it would keep being billed.
            await TryRemoveItemAsync(accessToken, exchange.ItemId, dropIfSaved: true);
            throw;
        }
    }

    public async Task DisconnectAsync(string userName, string connectionId)
    {
        var connection = await FindOwnedAsync(userName, connectionId);

        string? accessToken = null;
        try
        {
            accessToken = RevealAccessToken(connection);
        }
        catch (ApiRequestException)
        {
            // The token is gone, nothing can be removed at Plaid any more.
        }

        if (accessToken is not null)
        {
            try
            {
                await _plaid.CallAsync(c => c.ItemRemove(new ItemRemoveOperationRequest
                {
                    Body = new ItemRemoveRequest { AccessToken = accessToken }
                }));
            }
            catch (ApiRequestException ex) when (PlaidApi.CodeOf(ex) is "ITEM_NOT_FOUND" or "INVALID_ACCESS_TOKEN")
            {
                _logger.LogWarning("Plaid item {ItemId} was already removed", connection.ItemId);
            }
        }

        var checkouts = await _db.BankCheckouts
            .Where(c => c.BankConnectionId == connection.Id && c.Status == BankCheckoutStatus.Open).ToListAsync();
        _db.BankCheckouts.RemoveRange(checkouts);
        _db.BankConnections.Remove(connection);
        await _db.SaveChangesAsync();
    }

    public async Task<BankConnection> FindOwnedAsync(string userName, string connectionId)
    {
        if (!Guid.TryParse(connectionId, out var publicId))
        {
            throw new ApiRequestException(HttpStatusCode.NotFound, "The bank connection was not found.");
        }

        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.PublicId == publicId && c.UserName == userName);
        return connection ?? throw new ApiRequestException(HttpStatusCode.NotFound, "The bank connection was not found.");
    }

    private async Task TryRemoveItemAsync(string accessToken, string itemId, bool dropIfSaved)
    {
        try
        {
            if (dropIfSaved && await _db.BankConnections.AsNoTracking().AnyAsync(c => c.ItemId == itemId))
            {
                // Saved earlier (repeat of a known connection): that connection still owns the Item.
                return;
            }

            await _plaid.CallAsync(c => c.ItemRemove(new ItemRemoveOperationRequest
            {
                Body = new ItemRemoveRequest { AccessToken = accessToken }
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not remove the unsaved Plaid item {ItemId}", itemId);
        }
    }

    public static BankConnectionDto ToDto(BankConnection c) => new(
        c.PublicId.ToString(),
        c.Kind == BankConnectionKind.Brokerage ? BrokerageKind : BankKind,
        c.InstitutionId,
        c.InstitutionName,
        c.PaymentAccountName,
        c.PaymentAccountMask,
        c.CreatedAt);
}
