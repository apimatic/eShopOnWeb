using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Banking.Plaid;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record LinkTokenRequest(string? Type);
public record LinkTokenResponse(string LinkToken, string? Expiration, string Type);
public record SaveConnectionRequest(string? PublicToken, string? Type);
public record AccountInfo(string AccountId, string? Name, string? Mask, string? Type, string? Subtype);
public record ConnectionResponse(string ConnectionId, string Type, string? InstitutionId, string? InstitutionName, IReadOnlyList<AccountInfo> Accounts, DateTime ConnectedAt);

/// <summary>
/// Connecting, listing and disconnecting a shopper's bank / brokerage accounts through Plaid.
/// </summary>
public class BankConnectionService
{
    private const string ClientName = "eShopOnWeb";

    private readonly CatalogContext _db;
    private readonly PlaidClient _plaid;
    private readonly AccessTokenProtector _protector;
    private readonly KeyedLocks _locks;

    public BankConnectionService(CatalogContext db, PlaidClient plaid, AccessTokenProtector protector, KeyedLocks locks)
    {
        _db = db;
        _plaid = plaid;
        _protector = protector;
        _locks = locks;
    }

    /// <summary>Plaid rejects identifiers that look like personal data (an email), so it gets a stable opaque id.</summary>
    public static string PlaidUserId(string shopperId) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("eshop:" + shopperId))).ToLowerInvariant();

    public static ConnectionKind ParseKind(string? type) => (type?.Trim().ToLowerInvariant()) switch
    {
        null or "" or "bank" => ConnectionKind.Bank,
        "brokerage" or "investment" or "investments" => ConnectionKind.Brokerage,
        _ => throw BankApiException.BadRequest("type must be 'bank' or 'brokerage'.")
    };

    public async Task<LinkTokenResponse> CreateLinkTokenAsync(string shopperId, string? type, CancellationToken ct)
    {
        var kind = ParseKind(type);
        object body = kind == ConnectionKind.Bank
            ? new
            {
                client_name = ClientName,
                language = "en",
                country_codes = new[] { "US" },
                user = new { client_user_id = PlaidUserId(shopperId) },
                products = new[] { "transfer" },
                // so the support views of transactions never need a second consent
                additional_consented_products = new[] { "transactions" },
                account_filters = new { depository = new { account_subtypes = new[] { "checking", "savings" } } }
            }
            : new
            {
                client_name = ClientName,
                language = "en",
                country_codes = new[] { "US" },
                user = new { client_user_id = PlaidUserId(shopperId) },
                products = new[] { "investments" },
                account_filters = (object?)null
            };

        var response = await _plaid.PostAsync<PlaidLinkTokenResponse>("link/token/create", body, ct);
        return new LinkTokenResponse(response.LinkToken, response.Expiration, kind == ConnectionKind.Bank ? "bank" : "brokerage");
    }

    public async Task<ConnectionResponse> SaveAsync(string shopperId, SaveConnectionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PublicToken))
        {
            throw BankApiException.BadRequest("publicToken is required.");
        }
        ConnectionKind? requested = string.IsNullOrWhiteSpace(request.Type) ? null : ParseKind(request.Type);

        var exchange = await _plaid.PostAsync<PlaidExchangeResponse>("item/public_token/exchange",
            new { public_token = request.PublicToken.Trim() }, ct);

        if (await _db.BankConnections.AnyAsync(c => c.PlaidItemId == exchange.ItemId, ct))
        {
            // the same item can only already be known through a repeated request; never keep a second billed copy
            var existing = await _db.BankConnections.FirstAsync(c => c.PlaidItemId == exchange.ItemId, ct);
            if (existing.ShopperId != shopperId)
            {
                throw BankApiException.Conflict("This account is already connected.");
            }
            return ToResponse(existing);
        }

        try
        {
            var item = (await _plaid.PostAsync<PlaidItemResponse>("item/get", new { access_token = exchange.AccessToken }, ct)).Item;
            var accounts = (await _plaid.PostAsync<PlaidAccountsResponse>("accounts/get", new { access_token = exchange.AccessToken }, ct)).Accounts;

            var kind = requested ?? InferKind(item);
            var relevant = kind == ConnectionKind.Brokerage
                ? accounts.Where(a => a.Type == "investment")
                : accounts.Where(a => a.Type == "depository");
            var infos = relevant.Select(a => new AccountInfo(a.AccountId, a.Name, a.Mask, a.Type, a.Subtype)).ToList();

            var connection = new BankConnection
            {
                ShopperId = shopperId,
                Kind = kind,
                PlaidItemId = exchange.ItemId,
                AccessTokenProtected = _protector.Protect(exchange.AccessToken),
                InstitutionId = item.InstitutionId,
                InstitutionName = item.InstitutionName,
                AccountsJson = JsonSerializer.Serialize(infos)
            };
            _db.BankConnections.Add(connection);
            await _db.SaveChangesAsync(ct);
            return ToResponse(connection);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Do not keep paying Plaid for an item we failed to record.
            await TryRemoveItemAsync(exchange.AccessToken);
            throw;
        }
    }

    private static ConnectionKind InferKind(PlaidItem item)
    {
        var products = (item.Products ?? new()).Concat(item.BilledProducts ?? new()).ToHashSet();
        var investmentsOnly = products.Contains("investments")
            && !products.Overlaps(new[] { "transfer", "auth", "transactions", "balance", "identity", "signal" });
        return investmentsOnly ? ConnectionKind.Brokerage : ConnectionKind.Bank;
    }

    public async Task<List<ConnectionResponse>> ListAsync(string shopperId, CancellationToken ct)
    {
        var connections = await _db.BankConnections.AsNoTracking()
            .Where(c => c.ShopperId == shopperId).OrderBy(c => c.Id).ToListAsync(ct);
        return connections.Select(ToResponse).ToList();
    }

    public async Task DisconnectAsync(string shopperId, string connectionId, CancellationToken ct)
    {
        var connection = await FindOwnedAsync(shopperId, connectionId, ct)
            ?? throw BankApiException.NotFound("No such connection.");

        using var _ = await _locks.AcquireAsync("conn:" + connection.Id, ct);

        // Stop paying Plaid first: if that fails the connection stays so the shopper can retry.
        try
        {
            await _plaid.PostAsync<PlaidEmpty>("item/remove", new { access_token = _protector.Unprotect(connection.AccessTokenProtected) }, ct);
        }
        catch (PlaidException ex) when (ex.ErrorCode is "ITEM_NOT_FOUND" or "INVALID_ACCESS_TOKEN")
        {
            // already gone at Plaid
        }

        var transactions = await _db.BankTransactions.Where(t => t.ConnectionId == connection.Id).ToListAsync(ct);
        _db.BankTransactions.RemoveRange(transactions);
        var open = await _db.BankCheckouts
            .Where(c => c.ConnectionId == connection.Id && c.Status == BankCheckoutStatus.Started).ToListAsync(ct);
        foreach (var checkout in open)
        {
            checkout.Status = BankCheckoutStatus.Abandoned;
        }
        _db.BankConnections.Remove(connection);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<BankConnection?> FindOwnedAsync(string shopperId, string connectionId, CancellationToken ct)
    {
        if (!Guid.TryParse(connectionId, out var publicId))
        {
            return null;
        }
        return await _db.BankConnections.FirstOrDefaultAsync(c => c.PublicId == publicId && c.ShopperId == shopperId, ct);
    }

    public string AccessTokenOf(BankConnection connection) => _protector.Unprotect(connection.AccessTokenProtected);

    private async Task TryRemoveItemAsync(string accessToken)
    {
        try
        {
            await _plaid.PostAsync<PlaidEmpty>("item/remove", new { access_token = accessToken });
        }
        catch (Exception)
        {
            // best effort
        }
    }

    public static ConnectionResponse ToResponse(BankConnection c)
    {
        var accounts = JsonSerializer.Deserialize<List<AccountInfo>>(c.AccountsJson) ?? new();
        return new ConnectionResponse(c.PublicId.ToString(), c.Kind == ConnectionKind.Bank ? "bank" : "brokerage",
            c.InstitutionId, c.InstitutionName, accounts, c.CreatedAt);
    }
}
