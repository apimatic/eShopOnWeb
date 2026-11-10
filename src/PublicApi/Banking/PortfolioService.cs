using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using ThePlaidApi.Requests;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public sealed record PositionDto(
    string? Ticker,
    string? Name,
    decimal Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Quantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Contracts,
    string? Type,
    string? Currency,
    string? Brokerage,
    string? ConnectionId);

/// <summary>The holdings of the brokerage accounts a shopper connected.</summary>
public class PortfolioService
{
    /// <summary>Plaid reports an option's quantity in shares: one contract covers 100.</summary>
    private const decimal SharesPerOptionContract = 100m;

    private readonly PlaidApi _plaid;
    private readonly CatalogContext _db;
    private readonly BankConnectionService _connections;

    public PortfolioService(PlaidApi plaid, CatalogContext db, BankConnectionService connections)
    {
        _plaid = plaid;
        _db = db;
        _connections = connections;
    }

    public async Task<IReadOnlyList<PositionDto>> GetPositionsAsync(string userName)
    {
        var brokerages = await _db.BankConnections.AsNoTracking()
            .Where(c => c.UserName == userName && c.Kind == BankConnectionKind.Brokerage)
            .OrderBy(c => c.Id).ToListAsync();
        if (brokerages.Count == 0)
        {
            throw new ApiRequestException(HttpStatusCode.NotFound, "No brokerage account is connected.");
        }

        var positions = new List<PositionDto>();
        foreach (var brokerage in brokerages)
        {
            var accessToken = _connections.RevealAccessToken(brokerage);
            var response = await _plaid.CallAsync(c => c.InvestmentsHoldingsGet(new InvestmentsHoldingsGetOperationRequest
            {
                Body = new InvestmentsHoldingsGetRequest { AccessToken = accessToken }
            }));

            var securities = response.Securities.GroupBy(s => s.SecurityId).ToDictionary(g => g.Key, g => g.First());
            foreach (var holding in response.Holdings)
            {
                securities.TryGetValue(holding.SecurityId, out var security);
                var quantity = (decimal)holding.Quantity;
                var isOption = security is not null &&
                               (security.OptionContract is not null ||
                                string.Equals(security.Type, "derivative", StringComparison.OrdinalIgnoreCase));

                positions.Add(new PositionDto(
                    security?.TickerSymbol,
                    security?.Name,
                    Math.Round((decimal)holding.InstitutionValue, 2, MidpointRounding.AwayFromZero),
                    isOption ? null : quantity,
                    isOption ? quantity / SharesPerOptionContract : null,
                    security?.Type,
                    holding.IsoCurrencyCode ?? holding.UnofficialCurrencyCode,
                    brokerage.InstitutionName,
                    brokerage.PublicId.ToString()));
            }
        }

        return positions;
    }
}
