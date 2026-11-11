using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public sealed record BankDto(string Id, string Name, string? Url, string? PrimaryColor, bool Oauth);

public sealed record BankListDto(IReadOnlyList<BankDto> Banks, int Total, int Offset, int Count);

/// <summary>
/// The shop's bank picker. Every checkout opens it, so Plaid answers are cached and shared
/// by concurrent callers instead of being fetched again for each of them.
/// </summary>
public sealed class BankDirectoryService
{
    public const int MaxCount = 500;
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(15);
    private static readonly IReadOnlyList<CountryCode> Countries = new[] { CountryCode.Us };
    private static readonly IReadOnlyList<Products> PayByBankProducts = new[] { Products.Auth };

    private readonly PlaidGateway _plaid;
    private readonly IMemoryCache _cache;

    public BankDirectoryService(PlaidGateway plaid, IMemoryCache cache)
    {
        _plaid = plaid;
        _cache = cache;
    }

    public Task<BankListDto> GetBanksAsync(string? query, int? count, int? offset)
    {
        query = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var c = count ?? MaxCount;
        var o = offset ?? 0;
        if (c < 1 || c > MaxCount) throw new BankingException(400, $"count must be between 1 and {MaxCount}.");
        if (o < 0) throw new BankingException(400, "offset must not be negative.");

        var key = $"banks:{query?.ToLowerInvariant()}:{c}:{o}";
        var lazy = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTime;
            return new Lazy<Task<BankListDto>>(() => LoadAsync(query, c, o));
        })!;

        var task = lazy.Value;
        if (task.IsFaulted || task.IsCanceled)
            _cache.Remove(key);
        // A failure must not stay cached: evict once the shared task faults.
        task.ContinueWith(t => { if (t.IsFaulted || t.IsCanceled) _cache.Remove(key); }, TaskScheduler.Default);
        return task;
    }

    private async Task<BankListDto> LoadAsync(string? query, int count, int offset)
    {
        IReadOnlyList<Institution> institutions;
        int total;
        if (query is null)
        {
            var r = await _plaid.CallAsync(c => c.InstitutionsGet(new InstitutionsGetOperationRequest
            {
                Body = new InstitutionsGetRequest
                {
                    Count = count,
                    Offset = offset,
                    CountryCodes = Countries,
                    Options = new InstitutionsGetRequestOptions { Products = PayByBankProducts }
                }
            }));
            institutions = r.Institutions;
            total = r.Total;
        }
        else
        {
            var r = await _plaid.CallAsync(c => c.InstitutionsSearch(new InstitutionsSearchOperationRequest
            {
                Body = new InstitutionsSearchRequest
                {
                    Query = query,
                    CountryCodes = Countries,
                    Products = PayByBankProducts
                }
            }));
            // Plaid's search has no paging: it returns at most a screenful of best matches.
            total = r.Institutions.Count;
            institutions = r.Institutions.Skip(offset).Take(count).ToList();
        }

        var banks = institutions
            .Select(i => new BankDto(i.InstitutionId, i.Name, i.Url, i.PrimaryColor, i.Oauth))
            .ToList();
        return new BankListDto(banks, total, offset, banks.Count);
    }
}

public sealed record LinkTokenDto(string LinkToken, DateTimeOffset Expiration);

public sealed record ConnectionDto(
    int Id,
    string Kind,
    string? InstitutionId,
    string? InstitutionName,
    string? AccountId,
    string? AccountName,
    string? AccountMask);

public sealed class BankConnectionService
{
    private readonly PlaidGateway _plaid;
    private readonly CatalogContext _db;

    public BankConnectionService(PlaidGateway plaid, CatalogContext db)
    {
        _plaid = plaid;
        _db = db;
    }

    public static string NormalizeKind(string? kind) => (kind ?? BankConnectionKinds.Bank).Trim().ToLowerInvariant() switch
    {
        BankConnectionKinds.Bank => BankConnectionKinds.Bank,
        BankConnectionKinds.Brokerage => BankConnectionKinds.Brokerage,
        _ => throw new BankingException(400, "kind must be 'bank' or 'brokerage'.")
    };

    public async Task<LinkTokenDto> CreateLinkTokenAsync(ApplicationUser user, string? kind)
    {
        kind = NormalizeKind(kind);
        var request = new LinkTokenCreateRequest
        {
            ClientName = "eShopOnWeb",
            Language = "en",
            CountryCodes = new[] { CountryCode.Us },
            User = new LinkTokenCreateRequestUser { ClientUserId = user.Id },
        };
        request = kind == BankConnectionKinds.Brokerage
            ? request with { Products = new[] { Products.Investments } }
            // Auth for paying; the shopper also consents to transactions, so support can look at the activity.
            : request with
            {
                Products = new[] { Products.Auth },
                AdditionalConsentedProducts = new[] { Products.Transactions }
            };

        var response = await _plaid.CallAsync(c => c.LinkTokenCreate(new LinkTokenCreateOperationRequest { Body = request }));
        return new LinkTokenDto(response.LinkToken, response.Expiration);
    }

    public async Task<ConnectionDto> ConnectAsync(ApplicationUser user, string? publicToken, string? kind, string? accountId)
    {
        if (string.IsNullOrWhiteSpace(publicToken))
            throw new BankingException(400, "publicToken is required.");
        kind = NormalizeKind(kind);

        var exchange = await _plaid.CallAsync(c => c.ItemPublicTokenExchange(new ItemPublicTokenExchangeOperationRequest
        {
            Body = new ItemPublicTokenExchangeRequest { PublicToken = publicToken.Trim() }
        }));
        var accessToken = exchange.AccessToken;

        try
        {
            var accounts = await _plaid.CallAsync(c => c.AccountsGet(new AccountsGetOperationRequest
            {
                Body = new AccountsGetRequest { AccessToken = accessToken }
            }));

            var candidates = accounts.Accounts
                .Where(a => kind == BankConnectionKinds.Bank
                    ? a.Type == AccountType.Depository
                    : a.Type == AccountType.Investment || a.Type == AccountType.Brokerage)
                .ToList();

            Account? chosen = null;
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                chosen = candidates.FirstOrDefault(a => a.AccountId == accountId)
                    ?? throw new BankingException(422, kind == BankConnectionKinds.Bank
                        ? "accountId is not a checking or savings account of that connection."
                        : "accountId is not a brokerage account of that connection.");
            }
            else if (kind == BankConnectionKinds.Bank)
            {
                chosen = candidates.FirstOrDefault(a => a.Subtype?.Value == "checking") ?? candidates.FirstOrDefault();
            }

            if (candidates.Count == 0)
                throw new BankingException(422, kind == BankConnectionKinds.Bank
                    ? "That connection has no checking or savings account to pay from."
                    : "That connection has no brokerage account.");

            string? institutionId = accounts.Item.InstitutionId, institutionName = accounts.Item.InstitutionName;
            if (institutionId is null)
            {
                try
                {
                    var item = await _plaid.CallAsync(c => c.ItemGet(new ItemGetOperationRequest
                    {
                        Body = new ItemGetRequest { AccessToken = accessToken }
                    }));
                    institutionId = item.Item.InstitutionId;
                    institutionName ??= item.Item.InstitutionName;
                }
                catch (PlaidApiException)
                {
                }
            }

            if (institutionId is not null && institutionName is null)
            {
                try
                {
                    var inst = await _plaid.CallAsync(c => c.InstitutionsGetById(new InstitutionsGetByIdOperationRequest
                    {
                        Body = new InstitutionsGetByIdRequest { InstitutionId = institutionId, CountryCodes = new[] { CountryCode.Us } }
                    }));
                    institutionName = inst.Institution.Name;
                }
                catch (PlaidApiException)
                {
                }
            }

            var connection = new BankConnection
            {
                UserId = user.Id,
                Kind = kind,
                ItemId = exchange.ItemId,
                AccessToken = accessToken,
                InstitutionId = institutionId,
                InstitutionName = institutionName,
                AccountId = chosen?.AccountId,
                AccountName = chosen?.Name,
                AccountMask = chosen?.Mask,
            };
            _db.BankConnections.Add(connection);
            await _db.SaveChangesAsync();
            return ToDto(connection);
        }
        catch
        {
            // Do not leave an Item behind that nobody can use and that we would keep paying for.
            await TryRemoveItemAsync(accessToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<ConnectionDto>> ListAsync(ApplicationUser user)
    {
        var list = await _db.BankConnections.AsNoTracking().Where(c => c.UserId == user.Id).OrderBy(c => c.Id).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task DisconnectAsync(ApplicationUser user, int connectionId)
    {
        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == connectionId && c.UserId == user.Id)
            ?? throw new BankingException(404, "No such connection.");

        try
        {
            await _plaid.CallAsync(c => c.ItemRemove(new ItemRemoveOperationRequest
            {
                Body = new ItemRemoveRequest { AccessToken = connection.AccessToken }
            }));
        }
        catch (PlaidApiException ex) when (ex.ErrorCode is "ITEM_NOT_FOUND" or "INVALID_ACCESS_TOKEN")
        {
            // Plaid no longer has the Item: nothing left to pay for.
        }

        _db.BankConnections.Remove(connection); // the local copy of its activity goes with it
        await _db.SaveChangesAsync();
    }

    private async Task TryRemoveItemAsync(string accessToken)
    {
        try
        {
            await _plaid.CallAsync(c => c.ItemRemove(new ItemRemoveOperationRequest
            {
                Body = new ItemRemoveRequest { AccessToken = accessToken }
            }));
        }
        catch (Exception)
        {
        }
    }

    public static ConnectionDto ToDto(BankConnection c) =>
        new(c.Id, c.Kind, c.InstitutionId, c.InstitutionName, c.AccountId, c.AccountName, c.AccountMask);
}
