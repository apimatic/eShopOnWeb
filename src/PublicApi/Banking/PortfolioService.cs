using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

/// <summary>One holding. For an option <see cref="Contracts"/> is set; for anything else <see cref="Quantity"/> is.</summary>
public record Position(
    string? Ticker,
    string? Name,
    string? Type,
    decimal Value,
    decimal? Quantity,
    decimal? Contracts,
    string AccountId,
    string? Institution);

public class PortfolioService
{
    /// <summary>An option contract covers 100 units of the underlying.</summary>
    private const int OPTION_MULTIPLIER = 100;

    private static readonly TimeSpan NotReadyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NotReadyPoll = TimeSpan.FromSeconds(2);

    // OCC option symbol: underlying, yymmdd, C/P, strike*1000 (e.g. NFLX180201C00355000)
    private static readonly Regex OccSymbol = new(@"\d{6}[CP]\d{8}$", RegexOptions.Compiled);

    private readonly ThePlaidApiClient _plaid;
    private readonly CatalogContext _db;
    private readonly AccessTokenProtector _protector;

    public PortfolioService(ThePlaidApiClient plaid, CatalogContext db, AccessTokenProtector protector)
    {
        _plaid = plaid;
        _db = db;
        _protector = protector;
    }

    public async Task<IReadOnlyList<Position>> GetAsync(string buyerId, CancellationToken ct)
    {
        var buyer = BankConnectionService.NormalizeBuyer(buyerId);
        var connections = await _db.BankConnections.AsNoTracking()
            .Where(c => c.BuyerId == buyer && c.Kind == BankConnection.KIND_BROKERAGE && c.HasInvestmentAccounts)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);
        if (connections.Count == 0) throw new BankingException(404, "No brokerage account is connected.");

        var positions = new List<Position>();
        foreach (var connection in connections)
        {
            var accessToken = _protector.Unprotect(connection);
            var deadline = DateTimeOffset.UtcNow + NotReadyTimeout;

            InvestmentsHoldingsGetResponse response;
            while (true)
            {
                try
                {
                    response = await PlaidCall.Run(() => _plaid.InvestmentsHoldingsGet(new InvestmentsHoldingsGetOperationRequest
                    {
                        Body = new InvestmentsHoldingsGetRequest { AccessToken = accessToken },
                    }, cancellationToken: ct));
                    break;
                }
                catch (PlaidCallException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(NotReadyPoll, ct);
                }
            }

            var securities = response.Securities.GroupBy(s => s.SecurityId).ToDictionary(g => g.Key, g => g.First());
            foreach (var holding in response.Holdings)
            {
                securities.TryGetValue(holding.SecurityId, out var security);
                var quantity = (decimal)holding.Quantity;
                var value = Money.Round(Money.FromDouble(holding.InstitutionValue));

                if (security is not null && IsOption(security))
                {
                    // Plaid reports an option's quantity as the number of options (contracts x 100).
                    positions.Add(new Position(security.TickerSymbol, security.Name, security.Type, value,
                        null, quantity / OPTION_MULTIPLIER, holding.AccountId, connection.InstitutionName));
                }
                else
                {
                    positions.Add(new Position(security?.TickerSymbol, security?.Name, security?.Type, value,
                        quantity, null, holding.AccountId, connection.InstitutionName));
                }
            }
        }

        return positions;
    }

    private static bool IsOption(Security security)
    {
        if (security.OptionContract is not null) return true;
        if (string.Equals(security.Subtype, "option", StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(security.Type, "derivative", StringComparison.OrdinalIgnoreCase)
            && security.TickerSymbol is { } ticker && OccSymbol.IsMatch(ticker);
    }
}
