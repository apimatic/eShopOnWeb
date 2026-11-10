using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public class PositionDto
{
    public string? Ticker { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public decimal Value { get; set; }
    public string? CurrencyCode { get; set; }
    public string? Institution { get; set; }
    public string? AccountId { get; set; }
    /// <summary>Number of shares or units. Not set for options.</summary>
    public decimal? Quantity { get; set; }
    /// <summary>Number of contracts. Only set for options.</summary>
    public decimal? Contracts { get; set; }
}

public class PortfolioService
{
    private const int DefaultSharesPerContract = 100;
    private const int NotReadyAttempts = 8;

    private readonly PlaidClient _plaid;
    private readonly CatalogContext _db;

    public PortfolioService(PlaidClient plaid, CatalogContext db)
    {
        _plaid = plaid;
        _db = db;
    }

    public async Task<List<PositionDto>> GetPositionsAsync(string buyer, CancellationToken ct)
    {
        var id = BankConnectionService.NormalizeBuyer(buyer);
        var connections = await _db.BankConnections
            .Where(c => c.BuyerId == id && c.Type == BankConnectionTypes.Brokerage)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

        if (connections.Count == 0)
        {
            throw ApiProblemException.NotFound("No brokerage account is connected; connect one to see your portfolio.");
        }

        var positions = new List<PositionDto>();
        foreach (var connection in connections)
        {
            var json = await GetHoldingsAsync(connection, ct);
            var securities = (json["securities"]?.AsArray() ?? new JsonArray())
                .GroupBy(s => s!["security_id"]!.GetValue<string>())
                .ToDictionary(g => g.Key, g => g.First()!);

            foreach (var holding in json["holdings"]?.AsArray() ?? new JsonArray())
            {
                securities.TryGetValue(holding!["security_id"]!.GetValue<string>(), out var security);
                positions.Add(Map(holding, security, connection));
            }
        }

        return positions;
    }

    private static PositionDto Map(JsonNode holding, JsonNode? security, BankConnection connection)
    {
        var quantity = holding["quantity"]?.GetValue<decimal>() ?? 0m;
        var value = holding["institution_value"]?.GetValue<decimal>()
            ?? quantity * (holding["institution_price"]?.GetValue<decimal>() ?? 0m);

        var option = security?["option_contract"];
        var isOption = option is not null || security?["type"]?.GetValue<string>() == "derivative";

        var position = new PositionDto
        {
            Ticker = security?["ticker_symbol"]?.GetValue<string>(),
            Name = security?["name"]?.GetValue<string>() ?? string.Empty,
            Type = security?["type"]?.GetValue<string>(),
            Value = value,
            CurrencyCode = holding["iso_currency_code"]?.GetValue<string>() ?? holding["unofficial_currency_code"]?.GetValue<string>(),
            Institution = connection.InstitutionName,
            AccountId = holding["account_id"]?.GetValue<string>()
        };

        if (isOption)
        {
            // Plaid reports an option's quantity in shares of the underlying; a contract covers
            // shares_per_contract of them (100 unless Plaid says otherwise).
            var perContract = option?["shares_per_contract"]?.GetValue<decimal>() ?? DefaultSharesPerContract;
            position.Contracts = perContract > 0 ? quantity / perContract : quantity;
        }
        else
        {
            position.Quantity = quantity;
        }

        return position;
    }

    private async Task<JsonNode> GetHoldingsAsync(BankConnection connection, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _plaid.PostAsync("/investments/holdings/get", new { access_token = connection.AccessToken }, ct);
            }
            catch (PlaidApiException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && attempt < NotReadyAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }
}
