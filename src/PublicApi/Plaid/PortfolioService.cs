using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>The holdings of the shopper's connected brokerage accounts.</summary>
public class PortfolioService
{
    /// <summary>Standard equity option contract size: one contract covers 100 shares.</summary>
    public const decimal OptionMultiplier = 100m;

    private readonly PlaidClient _plaid;
    private readonly CatalogContext _db;
    private readonly ConnectionService _connections;

    public PortfolioService(PlaidClient plaid, CatalogContext db, ConnectionService connections)
    {
        _plaid = plaid;
        _db = db;
        _connections = connections;
    }

    public async Task<IResult> GetAsync(string userName, CancellationToken ct)
    {
        var owner = ApiResults.OwnerKey(userName);
        var brokerages = await _db.BankConnections.Include(c => c.Accounts)
            .Where(c => c.OwnerKey == owner && c.Kind == ConnectionKinds.Brokerage).OrderBy(c => c.Id).ToListAsync(ct);

        var positions = new List<Dictionary<string, object?>>();
        try
        {
            foreach (var connection in brokerages)
            {
                var result = await _plaid.InvestmentsHoldingsGetAsync(_connections.Unprotect(connection), ct);
                var securities = result.Arr("securities")
                    .Where(s => s.Str("security_id") is not null)
                    .GroupBy(s => s.Str("security_id")!).ToDictionary(g => g.Key, g => g.First());
                var accounts = result.Arr("accounts").ToDictionary(a => a.Str("account_id") ?? string.Empty, a => a.Str("name"));

                foreach (var holding in result.Arr("holdings"))
                {
                    securities.TryGetValue(holding.Str("security_id") ?? string.Empty, out var security);
                    var quantity = holding.Dec("quantity") ?? 0m;
                    var isOption = security.ValueKind == JsonValueKind.Object &&
                        (security.Str("type") == "derivative" || security.Obj("option_contract") is not null);

                    var position = new Dictionary<string, object?>
                    {
                        ["ticker"] = security.Str("ticker_symbol"),
                        ["name"] = security.Str("name"),
                        ["value"] = holding.Dec("institution_value"),
                        ["connectionId"] = connection.PublicId,
                        ["institutionName"] = connection.InstitutionName,
                        ["accountId"] = holding.Str("account_id"),
                        ["accountName"] = accounts.GetValueOrDefault(holding.Str("account_id") ?? string.Empty),
                        ["type"] = security.Str("type"),
                        ["isoCurrencyCode"] = holding.Str("iso_currency_code")
                    };
                    // Plaid counts options in underlying shares; the shopper holds contracts.
                    if (isOption) position["contracts"] = quantity / OptionMultiplier;
                    else position["quantity"] = quantity;
                    positions.Add(position);
                }
            }
        }
        catch (PlaidException ex)
        {
            return ApiResults.FromPlaid(ex);
        }

        return Results.Ok(new { positions });
    }
}
