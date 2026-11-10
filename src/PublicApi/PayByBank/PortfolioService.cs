using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.PayByBank;

/// <summary>
/// One holding. Options report <c>contracts</c>; everything else reports <c>quantity</c>
/// (the property that does not apply is null and left out of the JSON).
/// </summary>
public record PositionDto(
    int ConnectionId,
    string? InstitutionName,
    string AccountId,
    string? Ticker,
    string? Name,
    string? Type,
    decimal Value,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] decimal? Quantity,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] decimal? Contracts);

public class PortfolioService
{
    /// <summary>A standard equity option contract covers 100 units of the underlying.</summary>
    private const decimal OptionContractSize = 100m;

    private readonly CatalogContext _db;
    private readonly PlaidGateway _plaid;
    private readonly BankTokenProtector _tokens;

    public PortfolioService(CatalogContext db, PlaidGateway plaid, BankTokenProtector tokens)
    {
        _db = db;
        _plaid = plaid;
        _tokens = tokens;
    }

    /// <summary>
    /// Plaid describes options as <c>derivative</c> securities; the structured <c>option_contract</c> is only
    /// filled in when the institution supplies it (the sandbox, for one, leaves it null).
    /// </summary>
    private static bool IsOption(ThePlaidApi.Models.Security? security) =>
        security != null
        && (security.OptionContract != null
            || string.Equals(security.Type, "derivative", StringComparison.OrdinalIgnoreCase)
            || string.Equals(security.Subtype, "option", StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<PositionDto>> GetPositionsAsync(string userId)
    {
        var connections = await _db.BankConnections
            .Where(c => c.UserId == userId && c.Kind == BankConnectionKind.Brokerage)
            .OrderBy(c => c.Id).ToListAsync();

        var positions = new List<PositionDto>();
        foreach (var connection in connections)
        {
            var accessToken = _tokens.Unprotect(connection);
            var response = await PlaidRetry.WhenReadyAsync(() => _plaid.Call(c => c.InvestmentsHoldingsGet(new InvestmentsHoldingsGetOperationRequest
            {
                Body = new ThePlaidApi.Models.InvestmentsHoldingsGetRequest { AccessToken = accessToken }
            })));

            var securities = response.Securities.GroupBy(s => s.SecurityId).ToDictionary(g => g.Key, g => g.First());
            foreach (var holding in response.Holdings)
            {
                securities.TryGetValue(holding.SecurityId, out var security);
                var isOption = IsOption(security);
                var quantity = (decimal)holding.Quantity;

                positions.Add(new PositionDto(
                    connection.Id,
                    connection.InstitutionName,
                    holding.AccountId,
                    security?.TickerSymbol ?? security?.OptionContract?.UnderlyingSecurityTicker,
                    security?.Name,
                    security?.Type,
                    Money.FromDouble(holding.InstitutionValue),
                    // For options Plaid reports units (contracts x 100), not contracts.
                    isOption ? null : quantity,
                    isOption ? quantity / OptionContractSize : null));
            }
        }

        return positions;
    }

}
