using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record Position(int ConnectionId, string AccountId, string? Ticker, string? Name, decimal Value,
    decimal? Quantity, decimal? Contracts, string? Currency);

public class PortfolioService
{
    private readonly ThePlaidApiClient _plaid;
    private readonly ConnectionService _connections;

    public PortfolioService(ThePlaidApiClient plaid, ConnectionService connections)
    {
        _plaid = plaid;
        _connections = connections;
    }

    public async Task<IReadOnlyList<Position>> GetAsync(string shopperId, CancellationToken ct)
    {
        var brokerages = await _connections.ListActiveAsync(shopperId, BankConnectionKind.Brokerage, ct);
        if (brokerages.Count == 0)
            throw BankingException.NotFound("That shopper has not connected a brokerage account.");

        var positions = new List<Position>();
        foreach (var brokerage in brokerages)
        {
            var accessToken = _connections.AccessTokenOf(brokerage);
            var response = await PlaidCalls.WhenProductReady(() => _plaid.InvestmentsHoldingsGet(new InvestmentsHoldingsGetOperationRequest
            {
                Body = new InvestmentsHoldingsGetRequest { AccessToken = accessToken }
            }, cancellationToken: ct), ct);

            var securities = response.Securities.GroupBy(s => s.SecurityId).ToDictionary(g => g.Key, g => g.First());
            foreach (var holding in response.Holdings)
            {
                securities.TryGetValue(holding.SecurityId, out var security);
                var quantity = (decimal)holding.Quantity;
                var isOption = security is not null && IsOption(security);
                positions.Add(new Position(
                    brokerage.Id,
                    holding.AccountId,
                    security?.TickerSymbol,
                    security?.Name,
                    (decimal)holding.InstitutionValue,
                    // An option is held in contracts; everything else in shares/units.
                    isOption ? null : quantity,
                    isOption ? quantity / OptionContractSize : null,
                    holding.IsoCurrencyCode));
            }
        }

        return positions;
    }

    /// <summary>Plaid reports an option position in underlying shares; a standard contract covers 100 of them.</summary>
    internal const decimal OptionContractSize = 100m;

    private static readonly System.Text.RegularExpressions.Regex OccSymbol = new(@"^[A-Z0-9]{1,6}\d{6}[CP]\d{8}$");

    internal static bool IsOption(Security security)
    {
        if (security.OptionContract is not null)
            return true;
        var type = security.Type?.ToLowerInvariant();
        var subtype = security.Subtype?.ToLowerInvariant();
        return type is "option" or "derivative" || subtype is "call" or "put" || (security.TickerSymbol is not null && OccSymbol.IsMatch(security.TickerSymbol.Replace(" ", "")));
    }
}
