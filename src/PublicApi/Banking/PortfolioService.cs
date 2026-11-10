using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public class PositionDto
{
    public string? Ticker { get; set; }
    public string Name { get; set; } = "";
    public decimal Value { get; set; }

    /// <summary>For an option: the number of contracts held.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Contracts { get; set; }

    /// <summary>For anything else: how much of the security is held.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Quantity { get; set; }
}

public record PortfolioResult(List<PositionDto> Positions, List<UnavailableBank> Unavailable);

public partial class PortfolioService(ThePlaidApiClient plaid, CatalogContext db, AccessTokenProtector protector)
{
    /// <summary>Options trade in contracts of this many units.</summary>
    private const decimal ContractSize = 100m;

    // The OCC option symbol: underlying, yymmdd, C or P, strike
    [GeneratedRegex(@"^[A-Z0-9.]{1,6}\s*\d{6}[CP]\d{8}$")]
    private static partial Regex OccSymbol();

    public async Task<PortfolioResult> GetAsync(string userName)
    {
        var connections = await db.BankConnections
            .Where(c => c.UserName == userName && c.Kind == BankConnectionKind.Brokerage)
            .OrderBy(c => c.Id).ToListAsync();

        var positions = new List<PositionDto>();
        var unavailable = new List<UnavailableBank>();
        foreach (var connection in connections)
        {
            try
            {
                positions.AddRange(await HoldingsOfAsync(connection));
            }
            catch (Exception e) when (PlaidError.Translate(e) is { } failure)
            {
                unavailable.Add(new UnavailableBank(connection.Id, connection.InstitutionName, failure.Message));
            }
        }

        if (connections.Count > 0 && unavailable.Count == connections.Count)
        {
            throw new RequestFailedException(502, "The portfolio could not be retrieved: " + unavailable[0].Reason);
        }

        return new PortfolioResult(positions, unavailable);
    }

    private async Task<List<PositionDto>> HoldingsOfAsync(BankConnection connection)
    {
        var accessToken = protector.Unprotect(connection);
        var response = await PlaidError.WhenReady(() => plaid.InvestmentsHoldingsGet(new InvestmentsHoldingsGetOperationRequest
        {
            Body = new InvestmentsHoldingsGetRequest { AccessToken = accessToken },
        }));

        var securities = response.Securities.GroupBy(s => s.SecurityId).ToDictionary(g => g.Key, g => g.First());
        var positions = new List<PositionDto>();
        foreach (var holding in response.Holdings)
        {
            securities.TryGetValue(holding.SecurityId, out var security);
            var quantity = (decimal)holding.Quantity;
            var position = new PositionDto
            {
                Ticker = security?.TickerSymbol,
                Name = security?.Name ?? "",
                Value = Money.FromPlaid(holding.InstitutionValue),
            };

            if (IsOption(security))
            {
                // Plaid reports options as units (contracts x 100), not as contracts.
                position.Contracts = quantity / ContractSize;
            }
            else
            {
                position.Quantity = quantity;
            }

            positions.Add(position);
        }

        return positions;
    }

    private static bool IsOption(Security? security) =>
        security is not null
        && (security.OptionContract is not null
            || (security.Type == "derivative" && security.TickerSymbol is { } ticker && OccSymbol().IsMatch(ticker)));
}
