using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>
/// Thin client over Plaid's HTTP API. No SDK: each call POSTs JSON and returns the response document.
/// </summary>
public class PlaidClient
{
    private static readonly JsonSerializerOptions RequestJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<PlaidOptions> _options;

    public PlaidClient(HttpClient http, IOptionsMonitor<PlaidOptions> options)
    {
        _http = http;
        _options = options;
    }

    public async Task<JsonElement> PostAsync(string path, Dictionary<string, object?>? body = null, CancellationToken ct = default)
    {
        var options = _options.CurrentValue;
        if (!options.HasCredentials)
            throw new PlaidException("Plaid is not configured: Plaid:ClientId and Plaid:Secret are required.") { IsNotConfigured = true };
        var baseUrl = options.ResolveBaseUrl();
        if (baseUrl is null)
            throw new PlaidException("Plaid is not configured: Plaid:Environment must be sandbox or production (or set Plaid:BaseUrl).") { IsNotConfigured = true };

        body ??= new();
        body["client_id"] = options.ClientId;
        body["secret"] = options.Secret;

        // The base address is used verbatim: only the path is appended to it.
        var url = baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body, options: RequestJson)
        };
        request.Headers.Add("PLAID-CLIENT-ID", options.ClientId);
        request.Headers.Add("PLAID-SECRET", options.Secret);
        request.Headers.Add("Plaid-Version", "2020-09-14");

        HttpResponseMessage response;
        string text;
        try
        {
            response = await _http.SendAsync(request, ct);
            text = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && !ct.IsCancellationRequested)
        {
            throw new PlaidException($"Plaid could not be reached: {ex.Message}", inner: ex) { IsUnavailable = true };
        }

        using (response)
        {
            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(text); } catch (JsonException) { }
            var status = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                if (doc is not null && doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var root = doc.RootElement;
                    var ex = new PlaidException(root.Str("error_message") ?? $"Plaid answered {status}", status,
                        root.Str("error_type"), root.Str("error_code"), root.Str("display_message"))
                    {
                        IsUnavailable = status >= 500
                    };
                    doc.Dispose();
                    throw ex;
                }
                throw new PlaidException($"Plaid answered {status}", status) { IsUnavailable = status >= 500 };
            }

            if (doc is null)
                throw new PlaidException("Plaid answered with something that is not JSON.", status) { IsUnavailable = true };

            var clone = doc.RootElement.Clone();
            doc.Dispose();
            return clone;
        }
    }

    // ---- Institutions ----

    public Task<JsonElement> InstitutionsGetAsync(int count, int offset, string[] countryCodes, CancellationToken ct = default) =>
        PostAsync("institutions/get", new()
        {
            ["count"] = count,
            ["offset"] = offset,
            ["country_codes"] = countryCodes
        }, ct);

    public Task<JsonElement> InstitutionsSearchAsync(string query, string[] countryCodes, CancellationToken ct = default) =>
        PostAsync("institutions/search", new()
        {
            ["query"] = query,
            ["country_codes"] = countryCodes
        }, ct);

    // ---- Link / items ----

    public Task<JsonElement> LinkTokenCreateAsync(Dictionary<string, object?> body, CancellationToken ct = default) =>
        PostAsync("link/token/create", body, ct);

    public Task<JsonElement> ExchangePublicTokenAsync(string publicToken, CancellationToken ct = default) =>
        PostAsync("item/public_token/exchange", new() { ["public_token"] = publicToken }, ct);

    public Task<JsonElement> AccountsGetAsync(string accessToken, CancellationToken ct = default) =>
        PostAsync("accounts/get", new() { ["access_token"] = accessToken }, ct);

    public Task<JsonElement> ItemRemoveAsync(string accessToken, CancellationToken ct = default) =>
        PostAsync("item/remove", new() { ["access_token"] = accessToken }, ct);

    // ---- Transfer ----

    public Task<JsonElement> TransferAuthorizationCreateAsync(string accessToken, string accountId, string network,
        string amount, string legalName, CancellationToken ct = default) =>
        PostAsync("transfer/authorization/create", new()
        {
            ["access_token"] = accessToken,
            ["account_id"] = accountId,
            ["type"] = "debit",
            ["network"] = network,
            ["amount"] = amount,
            ["ach_class"] = "web",
            ["user"] = new Dictionary<string, object> { ["legal_name"] = legalName }
        }, ct);

    public Task<JsonElement> TransferCreateAsync(string accessToken, string accountId, string authorizationId,
        string description, CancellationToken ct = default) =>
        PostAsync("transfer/create", new()
        {
            ["access_token"] = accessToken,
            ["account_id"] = accountId,
            ["authorization_id"] = authorizationId,
            ["description"] = description
        }, ct);

    public Task<JsonElement> TransferGetAsync(string transferId, CancellationToken ct = default) =>
        PostAsync("transfer/get", new() { ["transfer_id"] = transferId }, ct);

    public Task<JsonElement> TransferCancelAsync(string transferId, CancellationToken ct = default) =>
        PostAsync("transfer/cancel", new() { ["transfer_id"] = transferId }, ct);

    public Task<JsonElement> TransferRefundCreateAsync(string transferId, string amount, string idempotencyKey, CancellationToken ct = default) =>
        PostAsync("transfer/refund/create", new()
        {
            ["transfer_id"] = transferId,
            ["amount"] = amount,
            ["idempotency_key"] = idempotencyKey
        }, ct);

    // ---- Transactions / investments ----

    public Task<JsonElement> TransactionsSyncAsync(string accessToken, string? cursor, CancellationToken ct = default) =>
        PostAsync("transactions/sync", new()
        {
            ["access_token"] = accessToken,
            ["cursor"] = cursor,
            ["count"] = 500
        }, ct);

    public Task<JsonElement> TransactionsGetAsync(string accessToken, string from, string to, int count, int offset, CancellationToken ct = default) =>
        PostAsync("transactions/get", new()
        {
            ["access_token"] = accessToken,
            ["start_date"] = from,
            ["end_date"] = to,
            ["options"] = new Dictionary<string, object> { ["count"] = count, ["offset"] = offset }
        }, ct);

    public Task<JsonElement> InvestmentsHoldingsGetAsync(string accessToken, CancellationToken ct = default) =>
        PostAsync("investments/holdings/get", new() { ["access_token"] = accessToken }, ct);
}

public static class JsonElementExtensions
{
    public static string? Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    public static decimal? Dec(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : null;

    public static bool Bool(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    public static JsonElement? Obj(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Object ? p : null;

    public static IEnumerable<JsonElement> Arr(this JsonElement e, string name)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array)
            foreach (var x in p.EnumerateArray()) yield return x;
    }
}
