using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public record BankDto(string InstitutionId, string Name, IReadOnlyList<string> Products);

/// <summary>
/// The banks Plaid supports. Every checkout opens the picker, so the full list (about 20 pages of 500)
/// is fetched once and cached; searching by name is done over the cached list.
/// </summary>
public class BankDirectory
{
    private readonly PlaidClient _plaid;
    private readonly object _sync = new();
    private List<BankDto>? _banks;
    private DateTime _loadedAt;
    private Task? _loading;

    private static readonly TimeSpan Fresh = TimeSpan.FromHours(6);

    public BankDirectory(PlaidClient plaid)
    {
        _plaid = plaid;
    }

    /// <summary>Starts (or joins) the one background load of the full list; it is not tied to any request.</summary>
    public Task Warm()
    {
        lock (_sync)
        {
            if (_loading == null || _loading.IsCompleted)
                _loading = Task.Run(FetchAllAsync);
            return _loading;
        }
    }

    private async Task FetchAllAsync()
    {
        try
        {
            var all = new List<BankDto>();
            var offset = 0;
            int total;
            do
            {
                JsonNode res;
                var tries = 0;
                while (true)
                {
                    try
                    {
                        res = await _plaid.PostAsync("/institutions/get", new JsonObject
                        {
                            ["count"] = 500,
                            ["offset"] = offset,
                            ["country_codes"] = new JsonArray("US"),
                        }, retry: true);
                        break;
                    }
                    catch (PlaidApiException e) when (e.ToStatusCode() == 429 && ++tries < 20)
                    {
                        // Plaid rate-limits this endpoint: back off and carry on from the same page.
                        await Task.Delay(TimeSpan.FromSeconds(5));
                    }
                }

                var page = res["institutions"]?.AsArray() ?? new JsonArray();
                foreach (var i in page)
                {
                    all.Add(new BankDto(
                        i!["institution_id"]!.GetValue<string>(),
                        i["name"]!.GetValue<string>(),
                        i["products"]?.AsArray().Select(p => p!.GetValue<string>()).ToList() ?? new List<string>()));
                }
                total = res["total"]?.GetValue<int>() ?? all.Count;
                offset += page.Count;
                if (page.Count == 0) break;
            } while (offset < total);

            all = all.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
            lock (_sync) { _banks = all; _loadedAt = DateTime.UtcNow; }
        }
        catch (Exception)
        {
            // Keep serving the previous list, if any; the next request tries again.
            lock (_sync) { _loading = null; }
            throw;
        }
    }

    private async Task<List<BankDto>> LoadAsync(CancellationToken ct)
    {
        List<BankDto>? banks; DateTime at;
        lock (_sync) { banks = _banks; at = _loadedAt; }
        if (banks != null)
        {
            if (DateTime.UtcNow - at > Fresh) _ = Warm().ContinueWith(_ => { }, TaskScheduler.Default); // refresh in the background
            return banks;
        }

        var load = Warm();
        var done = await Task.WhenAny(load, Task.Delay(TimeSpan.FromSeconds(30), ct));
        if (done == load)
        {
            await load; // surfaces a failure
            lock (_sync) { return _banks!; }
        }
        throw new AppException(503, "The bank list is still loading from Plaid; try again in a moment.");
    }

    public async Task<IReadOnlyList<BankDto>> SearchAsync(string? query, CancellationToken ct = default)
    {
        var all = await LoadAsync(ct);
        var terms = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return all;

        return all
            .Where(b => terms.All(t => b.Name.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(b => b.Name.StartsWith(terms[0], StringComparison.OrdinalIgnoreCase))
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
