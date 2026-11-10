using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using ThePlaidApi;
using ThePlaidApi.Models;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public record PositionDto(
    string? Ticker,
    string? Name,
    decimal Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Quantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Contracts,
    int ConnectionId,
    string? Institution);

public record PortfolioDto(IReadOnlyList<PositionDto> Positions, IReadOnlyList<BankStatusDto> Accounts);

public class PortfolioService
{
    private const int NotReadyPolls = 10;
    private static readonly TimeSpan NotReadyDelay = TimeSpan.FromSeconds(2);

    // An OCC option symbol, e.g. NFLX180201C00355000.
    private static readonly Regex OccSymbol = new("^[A-Z0-9.]{1,6}\\s*\\d{6}[CP]\\d{8}$", RegexOptions.Compiled);

    // Plaid reports an option's quantity as contracts x 100, not as the number of contracts.
    private const decimal DefaultSharesPerContract = 100m;

    private readonly ThePlaidApiClient _plaid;
    private readonly CatalogContext _db;
    private readonly AccessTokenProtector _protector;

    public PortfolioService(ThePlaidApiClient plaid, CatalogContext db, AccessTokenProtector protector)
    {
        _plaid = plaid;
        _db = db;
        _protector = protector;
    }

    public async Task<PortfolioDto> GetAsync(string userName, CancellationToken cancellationToken)
    {
        var key = CurrentUser.Key(userName);
        var connections = await _db.BankConnections.AsNoTracking()
            .Where(c => c.UserKey == key && c.Kind == BankConnectionKind.Brokerage)
            .OrderBy(c => c.Id).ToListAsync(cancellationToken).ConfigureAwait(false);

        var positions = new List<PositionDto>();
        var statuses = new List<BankStatusDto>();
        ApiProblemException? firstProblem = null;
        foreach (var connection in connections)
        {
            try
            {
                var holdings = await LoadHoldingsAsync(_protector.Unprotect(connection.ProtectedAccessToken), cancellationToken)
                    .ConfigureAwait(false);
                var securities = holdings.Securities.GroupBy(s => s.SecurityId).ToDictionary(g => g.Key, g => g.First());
                foreach (var holding in holdings.Holdings)
                {
                    securities.TryGetValue(holding.SecurityId, out var security);
                    positions.Add(ToPosition(holding, security, connection));
                }
                statuses.Add(new BankStatusDto(connection.Id, connection.InstitutionName, true, null));
            }
            catch (ApiProblemException ex)
            {
                firstProblem ??= ex;
                statuses.Add(new BankStatusDto(connection.Id, connection.InstitutionName, false, ex.Message));
            }
        }

        if (firstProblem != null && statuses.All(s => !s.UpToDate))
            throw firstProblem;

        return new PortfolioDto(positions, statuses);
    }

    private async Task<InvestmentsHoldingsGetResponse> LoadHoldingsAsync(string accessToken, CancellationToken cancellationToken)
    {
        for (var poll = 0; ; poll++)
        {
            try
            {
                return await PlaidCall.RunAsync(() => _plaid.InvestmentsHoldingsGet(new()
                {
                    Body = new() { AccessToken = accessToken }
                })).ConfigureAwait(false);
            }
            catch (Exception ex) when (PlaidCall.IsCode(ex, "PRODUCT_NOT_READY") && poll < NotReadyPolls)
            {
                await Task.Delay(NotReadyDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static PositionDto ToPosition(Holding holding, Security? security, BankConnection connection)
    {
        var value = (decimal)holding.InstitutionValue;
        var quantity = (decimal)holding.Quantity;
        if (IsOption(security))
        {
            return new PositionDto(security?.TickerSymbol, security?.Name, value, null, quantity / DefaultSharesPerContract,
                connection.Id, connection.InstitutionName);
        }
        return new PositionDto(security?.TickerSymbol, security?.Name, value, quantity, null,
            connection.Id, connection.InstitutionName);
    }

    public static bool IsOption(Security? security)
    {
        if (security == null)
            return false;
        if (security.OptionContract != null)
            return true;
        if (string.Equals(security.Subtype, "option", StringComparison.OrdinalIgnoreCase))
            return true;
        // Plaid doesn't always fill in the option details: a derivative named by an OCC symbol is an option.
        return string.Equals(security.Type, "derivative", StringComparison.OrdinalIgnoreCase) &&
               security.TickerSymbol != null && OccSymbol.IsMatch(security.TickerSymbol.Trim().ToUpperInvariant());
    }
}
