using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public sealed record PositionDto(
    string? Ticker,
    string? Name,
    decimal Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Quantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Contracts,
    string? IsoCurrencyCode,
    int ConnectionId,
    string? InstitutionName,
    string AccountId);

public sealed record PortfolioDto(IReadOnlyList<PositionDto> Positions, IReadOnlyList<SyncProblemDto> Problems);

public sealed class PortfolioService
{
    /// <summary>Plaid reports an option's quantity as options, and one contract covers 100 of them.</summary>
    private const decimal OptionsPerContract = 100m;

    private readonly PlaidGateway _plaid;
    private readonly CatalogContext _db;

    public PortfolioService(PlaidGateway plaid, CatalogContext db)
    {
        _plaid = plaid;
        _db = db;
    }

    /// <summary>Plaid does not always fill option_contract (the sandbox does not): an option is also a 'derivative'.</summary>
    private static bool IsOption(ThePlaidApi.Models.Security? security)
    {
        if (security is null) return false;
        if (security.OptionContract is not null) return true;
        if (!string.Equals(security.Type, "derivative", StringComparison.OrdinalIgnoreCase)) return false;
        var subtype = security.Subtype?.ToLowerInvariant();
        return subtype is null || !(subtype.Contains("future") || subtype.Contains("warrant") || subtype.Contains("right"));
    }

    public async Task<PortfolioDto> GetAsync(ApplicationUser user)
    {
        var connections = await _db.BankConnections.AsNoTracking()
            .Where(c => c.UserId == user.Id && c.Kind == BankConnectionKinds.Brokerage).OrderBy(c => c.Id).ToListAsync();

        var positions = new List<PositionDto>();
        var problems = new List<SyncProblemDto>();
        foreach (var connection in connections)
        {
            try
            {
                var response = await _plaid.CallAsync(c => c.InvestmentsHoldingsGet(new InvestmentsHoldingsGetOperationRequest
                {
                    Body = new ThePlaidApi.Models.InvestmentsHoldingsGetRequest { AccessToken = connection.AccessToken }
                }));
                var securities = response.Securities.ToDictionary(s => s.SecurityId);
                foreach (var h in response.Holdings)
                {
                    if (connection.AccountId is not null && h.AccountId != connection.AccountId) continue;
                    securities.TryGetValue(h.SecurityId, out var security);
                    var quantity = Convert.ToDecimal(h.Quantity, CultureInfo.InvariantCulture);
                    var isOption = IsOption(security);
                    positions.Add(new PositionDto(
                        security?.TickerSymbol,
                        security?.Name,
                        Convert.ToDecimal(h.InstitutionValue, CultureInfo.InvariantCulture),
                        isOption ? null : quantity,
                        isOption ? quantity / OptionsPerContract : null,
                        h.IsoCurrencyCode,
                        connection.Id,
                        connection.InstitutionName,
                        h.AccountId));
                }
            }
            catch (PlaidApiException ex)
            {
                problems.Add(new SyncProblemDto(connection.Id, connection.InstitutionName, ex.Message));
            }
        }

        return new PortfolioDto(positions, problems);
    }
}
