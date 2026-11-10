using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public record ConnectionProblem(Guid ConnectionId, string? InstitutionName, string Reason);

public class ActivityService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SyncLocks = new();

    private readonly PlaidClient _plaid;
    private readonly CatalogContext _db;

    public ActivityService(PlaidClient plaid, CatalogContext db)
    {
        _plaid = plaid;
        _db = db;
    }

    private static decimal Dec(JsonNode? n) => n == null ? 0m : n.GetValue<decimal>();

    private static string Describe(Exception e) => e is AppException a ? a.Message : e.Message;

    private async Task<List<BankConnection>> BankConnectionsAsync(string buyerId, CancellationToken ct) =>
        await _db.BankConnections
            .Where(c => c.BuyerId == buyerId && c.Kind == ConnectionKinds.Bank && c.DisconnectedAt == null)
            .OrderBy(c => c.CreatedAt).ToListAsync(ct);

    // ---- Bank activity: the shop's own copy, kept in step with /transactions/sync ----

    public async Task<object> BankActivityAsync(string buyerId, CancellationToken ct)
    {
        var connections = await BankConnectionsAsync(buyerId, ct);
        var problems = new List<ConnectionProblem>();

        foreach (var conn in connections)
        {
            try
            {
                await SyncAsync(conn, ct);
            }
            catch (Exception e) when (e is PlaidApiException or AppException)
            {
                // Show what we have, and say the copy may be behind.
                problems.Add(new ConnectionProblem(conn.Id, conn.InstitutionName, Describe(e)));
            }
        }

        var ids = connections.Select(c => c.Id).ToList();
        var rows = await _db.BankTransactions.AsNoTracking().Where(t => ids.Contains(t.ConnectionId)).ToListAsync(ct);
        var names = connections.ToDictionary(c => c.Id, c => c.InstitutionName);
        var transactions = rows
            .OrderByDescending(t => t.Date).ThenBy(t => t.PlaidTransactionId, StringComparer.Ordinal)
            .Select(t => new
            {
                name = t.Name,
                amount = t.Amount,
                date = DateOnly.FromDateTime(t.Date),
                pending = t.Pending,
                accountId = t.AccountId,
                connectionId = t.ConnectionId,
                institutionName = names[t.ConnectionId],
                isoCurrencyCode = t.IsoCurrencyCode,
            }).ToList();

        return new { transactions, notUpToDate = problems };
    }

    private async Task SyncAsync(BankConnection conn, CancellationToken ct)
    {
        var sem = SyncLocks.GetOrAdd(conn.Id, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            var startCursor = conn.SyncCursor;
            var attempts = 0;
            while (true)
            {
                var added = new List<JsonNode>();
                var modified = new List<JsonNode>();
                var removed = new List<string>();
                var cursor = startCursor;
                try
                {
                    bool more;
                    do
                    {
                        var captured = cursor;
                        var res = await _plaid.PostWhenReadyAsync("/transactions/sync", () =>
                        {
                            var b = new JsonObject { ["access_token"] = conn.AccessToken, ["count"] = 500 };
                            if (!string.IsNullOrEmpty(captured)) b["cursor"] = captured;
                            return b;
                        }, ct: ct);
                        added.AddRange(res["added"]!.AsArray()!.Select(x => x!));
                        modified.AddRange(res["modified"]!.AsArray()!.Select(x => x!));
                        removed.AddRange(res["removed"]!.AsArray().Select(x => x!["transaction_id"]!.GetValue<string>()));
                        cursor = res["next_cursor"]!.GetValue<string>();
                        more = res["has_more"]!.GetValue<bool>();
                    } while (more);
                }
                catch (PlaidApiException e) when (e.ErrorCode == "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION" && ++attempts < 3)
                {
                    continue; // data changed mid-pagination: start over from the cursor we began with
                }

                await ApplyAsync(conn, added, modified, removed, cursor, ct);
                return;
            }
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task ApplyAsync(BankConnection conn, List<JsonNode> added, List<JsonNode> modified, List<string> removed, string? cursor, CancellationToken ct)
    {
        var existing = await _db.BankTransactions.Where(t => t.ConnectionId == conn.Id).ToDictionaryAsync(t => t.PlaidTransactionId, ct);

        foreach (var id in removed)
        {
            if (existing.Remove(id, out var gone)) _db.BankTransactions.Remove(gone);
        }
        foreach (var n in added.Concat(modified))
        {
            var id = n["transaction_id"]!.GetValue<string>();
            if (!existing.TryGetValue(id, out var row))
            {
                row = new BankTransaction { ConnectionId = conn.Id, PlaidTransactionId = id };
                _db.BankTransactions.Add(row);
                existing[id] = row;
            }
            row.AccountId = n["account_id"]!.GetValue<string>();
            row.Name = n["name"]?.GetValue<string>() ?? n["merchant_name"]?.GetValue<string>() ?? "";
            row.Amount = Dec(n["amount"]);
            row.IsoCurrencyCode = n["iso_currency_code"]?.GetValue<string>();
            row.Date = DateTime.ParseExact(n["date"]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            row.Pending = n["pending"]?.GetValue<bool>() ?? false;
            row.PendingTransactionId = n["pending_transaction_id"]?.GetValue<string>();
        }
        conn.SyncCursor = cursor;
        await _db.SaveChangesAsync(ct);
    }

    // ---- Transactions between two dates, straight from the bank ----

    public async Task<object> TransactionsBetweenAsync(string buyerId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var connections = await BankConnectionsAsync(buyerId, ct);
        var transactions = new List<(DateOnly date, object row)>();
        var problems = new List<ConnectionProblem>();

        foreach (var conn in connections)
        {
            try
            {
                var offset = 0;
                int total;
                do
                {
                    var o = offset;
                    var res = await _plaid.PostWhenReadyAsync("/transactions/get", () => new JsonObject
                    {
                        ["access_token"] = conn.AccessToken,
                        ["start_date"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["end_date"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["options"] = new JsonObject { ["count"] = 500, ["offset"] = o },
                    }, ct: ct);
                    var page = res["transactions"]!.AsArray();
                    foreach (var t in page)
                    {
                        var date = DateOnly.ParseExact(t!["date"]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                        transactions.Add((date, new
                        {
                            name = t["name"]?.GetValue<string>() ?? "",
                            amount = Dec(t["amount"]),
                            date,
                            accountId = t["account_id"]!.GetValue<string>(),
                            connectionId = conn.Id,
                            institutionName = conn.InstitutionName,
                            isoCurrencyCode = t["iso_currency_code"]?.GetValue<string>(),
                        }));
                    }
                    total = res["total_transactions"]!.GetValue<int>();
                    offset += page.Count;
                    if (page.Count == 0) break;
                } while (offset < total);
            }
            catch (Exception e) when (e is PlaidApiException or AppException)
            {
                problems.Add(new ConnectionProblem(conn.Id, conn.InstitutionName, Describe(e)));
            }
        }

        if (connections.Count > 0 && problems.Count == connections.Count)
            throw new AppException(409, "No connected bank could be read: " + problems[0].Reason);

        return new
        {
            transactions = transactions.OrderByDescending(t => t.date).Select(t => t.row).ToList(),
            unavailable = problems,
        };
    }

    // ---- Portfolio ----

    public async Task<object> PortfolioAsync(string buyerId, CancellationToken ct)
    {
        var connections = await _db.BankConnections
            .Where(c => c.BuyerId == buyerId && c.Kind == ConnectionKinds.Brokerage && c.DisconnectedAt == null)
            .OrderBy(c => c.CreatedAt).ToListAsync(ct);

        var positions = new List<object>();
        var problems = new List<ConnectionProblem>();

        foreach (var conn in connections)
        {
            try
            {
                var res = await _plaid.PostWhenReadyAsync("/investments/holdings/get",
                    () => new JsonObject { ["access_token"] = conn.AccessToken }, ct: ct);
                var securities = res["securities"]!.AsArray().ToDictionary(s => s!["security_id"]!.GetValue<string>(), s => s!);
                var investmentAccounts = res["accounts"]!.AsArray()
                    .Where(a => a!["type"]?.GetValue<string>() == "investment")
                    .Select(a => a!["account_id"]!.GetValue<string>()).ToHashSet();

                foreach (var h in res["holdings"]!.AsArray())
                {
                    if (!investmentAccounts.Contains(h!["account_id"]!.GetValue<string>())) continue;
                    var sec = securities[h["security_id"]!.GetValue<string>()];
                    var quantity = Dec(h["quantity"]);
                    var value = Dec(h["institution_value"]);
                    var ticker = sec["ticker_symbol"]?.GetValue<string>();
                    var name = sec["name"]?.GetValue<string>();
                    var isOption = (sec["option_contract"] is JsonObject) || sec["type"]?.GetValue<string>() == "derivative";

                    if (isOption)
                    {
                        // Plaid reports an option's quantity as the underlying units (100 per contract).
                        positions.Add(new { ticker, name, value, contracts = quantity / 100m, connectionId = conn.Id, institutionName = conn.InstitutionName });
                    }
                    else
                    {
                        positions.Add(new { ticker, name, value, quantity, connectionId = conn.Id, institutionName = conn.InstitutionName });
                    }
                }
            }
            catch (Exception e) when (e is PlaidApiException or AppException)
            {
                problems.Add(new ConnectionProblem(conn.Id, conn.InstitutionName, Describe(e)));
            }
        }

        if (connections.Count > 0 && problems.Count == connections.Count)
            throw new AppException(409, "Your brokerage could not be read: " + problems[0].Reason);

        return new { holdings = positions, positions, unavailable = problems };
    }
}
