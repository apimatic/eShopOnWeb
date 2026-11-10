using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.PublicApi.Plaid.Data;
using ThePlaidApi.Models;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Plaid.Services;

public sealed record Position(string? Ticker, string? Name, decimal Value, decimal? Quantity, decimal? Contracts,
    Guid ConnectionId, string InstitutionName, string AccountId);

public sealed class PortfolioService
{
    /// <summary>An exchange-traded option contract covers this many units of the underlying.</summary>
    private const decimal ContractMultiplier = 100m;

    // OCC option symbology, e.g. NFLX180201C00355000.
    private static readonly Regex OccSymbol = new(@"^[A-Z0-9]{1,6}\d{6}[CP]\d{8}$", RegexOptions.Compiled);

    private readonly PlaidContext _db;
    private readonly PlaidAccess _plaid;

    public PortfolioService(PlaidContext db, PlaidAccess plaid)
    {
        _db = db;
        _plaid = plaid;
    }

    public async Task<IReadOnlyList<Position>> GetAsync(string userName, CancellationToken ct)
    {
        var user = BankConnectionService.Normalize(userName);
        var connections = await _db.Connections.AsNoTracking()
            .Where(c => c.UserName == user && c.IsBrokerage).OrderBy(c => c.CreatedAt).ToListAsync(ct);
        if (connections.Count == 0)
            throw BankApiException.NotFound("No brokerage account is connected.");

        var positions = new List<Position>();
        var failures = new List<BankApiException>();
        foreach (var connection in connections)
        {
            try
            {
                var token = _plaid.Unprotect(connection.ProtectedAccessToken);
                var response = await _plaid.WhenProductReady(() => _plaid.Client.InvestmentsHoldingsGet(
                    new InvestmentsHoldingsGetOperationRequest { Body = new InvestmentsHoldingsGetRequest { AccessToken = token } }, null, ct), ct);
                var securities = response.Securities.GroupBy(s => s.SecurityId).ToDictionary(g => g.Key, g => g.First());

                foreach (var holding in response.Holdings)
                {
                    securities.TryGetValue(holding.SecurityId, out var security);
                    var quantity = (decimal)holding.Quantity;
                    var isOption = security is not null && IsOption(security);
                    positions.Add(new Position(
                        security?.TickerSymbol,
                        security?.Name,
                        decimal.Round((decimal)holding.InstitutionValue, 2),
                        isOption ? null : quantity,
                        // Plaid reports an option's holding in units of the underlying; a contract is 100 of them.
                        isOption ? quantity / ContractMultiplier : null,
                        connection.Id, connection.InstitutionName, holding.AccountId));
                }
            }
            catch (Exception e) when (PlaidFailure.ToApiException(e) is { } mapped)
            {
                // A single broker being unreachable should not hide the others; fail only if none could be read.
                failures.Add(mapped);
            }
        }
        if (failures.Count == connections.Count) throw failures[0];
        return positions;
    }

    private static bool IsOption(Security s) =>
        s.OptionContract is not null
        || string.Equals(s.Subtype, "option", StringComparison.OrdinalIgnoreCase)
        || (string.Equals(s.Type, "derivative", StringComparison.OrdinalIgnoreCase)
            && s.TickerSymbol is not null && OccSymbol.IsMatch(s.TickerSymbol));
}
