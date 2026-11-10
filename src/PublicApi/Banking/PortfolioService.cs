using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Banking.Plaid;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public class PortfolioPosition
{
    public string? Ticker { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public decimal Value { get; set; }
    public string? CurrencyCode { get; set; }
    /// <summary>Units held, for anything that is not an option.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Quantity { get; set; }
    /// <summary>Number of contracts held, for an option.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Contracts { get; set; }
    public string? AccountId { get; set; }
    public string ConnectionId { get; set; } = string.Empty;
    public string? Institution { get; set; }
}

public class PortfolioResponse
{
    public IReadOnlyList<PortfolioPosition> Positions { get; set; } = Array.Empty<PortfolioPosition>();
    /// <summary>The same list as <see cref="Positions"/>.</summary>
    public IReadOnlyList<PortfolioPosition> Holdings => Positions;
    public IReadOnlyList<SyncWarning> Warnings { get; set; } = Array.Empty<SyncWarning>();
}

/// <summary>
/// A shopper's holdings across their connected brokerage accounts.
/// </summary>
public class PortfolioService
{
    /// <summary>Shares of the underlying covered by one standard equity option contract.</summary>
    private const decimal OptionMultiplier = 100m;

    private static readonly TimeSpan[] NotReadyDelays =
        { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5) };

    private readonly CatalogContext _db;
    private readonly PlaidClient _plaid;
    private readonly AccessTokenProtector _protector;

    public PortfolioService(CatalogContext db, PlaidClient plaid, AccessTokenProtector protector)
    {
        _db = db;
        _plaid = plaid;
        _protector = protector;
    }

    public async Task<PortfolioResponse> GetAsync(string shopperId, CancellationToken ct)
    {
        var connections = await _db.BankConnections.AsNoTracking()
            .Where(c => c.ShopperId == shopperId && c.Kind == ConnectionKind.Brokerage).OrderBy(c => c.Id).ToListAsync(ct);

        var positions = new List<PortfolioPosition>();
        var warnings = new List<SyncWarning>();
        PlaidException? firstError = null;

        foreach (var connection in connections)
        {
            try
            {
                var accessToken = _protector.Unprotect(connection.AccessTokenProtected);
                var holdings = await WithNotReadyRetryAsync(
                    () => _plaid.PostAsync<PlaidHoldingsResponse>("investments/holdings/get", new { access_token = accessToken }, ct), ct);
                positions.AddRange(Map(holdings, connection));
            }
            catch (PlaidException ex)
            {
                firstError ??= ex;
                warnings.Add(new SyncWarning(connection.PublicId.ToString(), connection.InstitutionName, PlaidErrorMapper.Map(ex).Reason));
            }
        }

        if (connections.Count > 0 && warnings.Count == connections.Count && firstError is not null)
        {
            throw firstError;
        }

        return new PortfolioResponse { Positions = positions, Warnings = warnings };
    }

    public static IEnumerable<PortfolioPosition> Map(PlaidHoldingsResponse response, BankConnection connection)
    {
        var securities = response.Securities.GroupBy(s => s.SecurityId).ToDictionary(g => g.Key, g => g.First());
        foreach (var holding in response.Holdings)
        {
            securities.TryGetValue(holding.SecurityId, out var security);
            var value = holding.InstitutionValue ?? (holding.InstitutionPrice ?? 0m) * holding.Quantity;
            var position = new PortfolioPosition
            {
                Ticker = security?.TickerSymbol,
                Name = security?.Name,
                Type = security?.Type,
                Value = value,
                CurrencyCode = holding.IsoCurrencyCode ?? holding.UnofficialCurrencyCode,
                AccountId = holding.AccountId,
                ConnectionId = connection.PublicId.ToString(),
                Institution = connection.InstitutionName
            };

            if (IsOption(security))
            {
                position.Contracts = ContractsHeld(holding);
            }
            else
            {
                position.Quantity = holding.Quantity;
            }
            yield return position;
        }
    }

    private static bool IsOption(PlaidSecurity? security) =>
        security is not null && (security.OptionContract is not null
            || string.Equals(security.Type, "derivative", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Institutions report an option's quantity either as contracts or as underlying shares (contracts x 100).
    /// The reported value tells which: value = quantity x price x 100 when it is contracts, quantity x price when shares.
    /// </summary>
    public static decimal ContractsHeld(PlaidHolding holding)
    {
        var quantity = holding.Quantity;
        if (holding.InstitutionPrice is { } price && holding.InstitutionValue is { } value && price != 0m && quantity != 0m)
        {
            var asShares = Math.Abs(quantity * price - value);
            var asContracts = Math.Abs(quantity * price * OptionMultiplier - value);
            if (asShares <= asContracts)
            {
                return quantity / OptionMultiplier;
            }
            return quantity;
        }
        return quantity;
    }

    private static async Task<T> WithNotReadyRetryAsync<T>(Func<Task<T>> call, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (PlaidException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && attempt < NotReadyDelays.Length)
            {
                await Task.Delay(NotReadyDelays[attempt], ct);
            }
        }
    }
}
