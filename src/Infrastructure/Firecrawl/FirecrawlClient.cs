using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

internal sealed class FirecrawlClient : IFirecrawlClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> TerminalStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "completed", "failed", "cancelled" };

    private readonly HttpClient _httpClient;
    private readonly FirecrawlOptions _options;
    private readonly IAppLogger<FirecrawlClient> _logger;

    public FirecrawlClient(HttpClient httpClient, IOptions<FirecrawlOptions> options, IAppLogger<FirecrawlClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FirecrawlCrawlResult> CrawlAsync(CrawlRequest request, CancellationToken cancellationToken = default)
    {
        var id = await StartCrawlAsync(request, cancellationToken);
        _logger.LogInformation($"Firecrawl crawl {id} started for {request.Url}.");

        var terminal = await PollUntilTerminalAsync(id, cancellationToken);
        var data = await AccumulateDataAsync(id, terminal, cancellationToken);

        return new FirecrawlCrawlResult
        {
            Status = terminal.Status ?? "failed",
            Total = terminal.Total,
            Completed = terminal.Completed,
            Data = data
        };
    }

    private async Task<string> StartCrawlAsync(CrawlRequest request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("crawl", request, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<CrawlStartResponse>(SerializerOptions, cancellationToken);
        if (body?.Id is null || string.IsNullOrWhiteSpace(body.Id))
        {
            throw new FirecrawlApiException(response.StatusCode, null,
                "Firecrawl accepted the crawl request but returned no crawl id.");
        }
        return body.Id;
    }

    private async Task<CrawlStatusResponse> PollUntilTerminalAsync(string id, CancellationToken cancellationToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.CrawlTimeoutSeconds));
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            var status = await GetStatusAsync(id, skip: null, cancellationToken);
            if (status.Status is not null && TerminalStatuses.Contains(status.Status))
            {
                _logger.LogInformation(
                    $"Firecrawl crawl {id} reached '{status.Status}' ({status.Completed}/{status.Total} pages).");
                return status;
            }

            if (stopwatch.Elapsed > timeout)
            {
                throw new FirecrawlApiException(HttpStatusCode.RequestTimeout, "CRAWL_TIMEOUT",
                    $"Firecrawl crawl {id} did not finish within {_options.CrawlTimeoutSeconds}s " +
                    $"(last status '{status.Status}', {status.Completed}/{status.Total} pages).");
            }

            await Task.Delay(pollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// Pages through the full result set. The status response returns the first chunk of
    /// <c>data</c> plus a <c>next</c> link when more remains; we follow it using the spec's
    /// <c>skip</c> query parameter, always against the configured base address.
    /// </summary>
    private async Task<List<CrawlDataItem>> AccumulateDataAsync(
        string id, CrawlStatusResponse terminal, CancellationToken cancellationToken)
    {
        var all = new List<CrawlDataItem>();
        if (terminal.Data is not null)
        {
            all.AddRange(terminal.Data);
        }

        var next = terminal.Next;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(next) && guard++ < 10_000)
        {
            var skip = TryParseSkip(next);
            if (skip is null)
            {
                break;
            }

            var page = await GetStatusAsync(id, skip, cancellationToken);
            if (page.Data is null || page.Data.Count == 0)
            {
                break;
            }
            all.AddRange(page.Data);
            next = page.Next;
        }

        return all;
    }

    private async Task<CrawlStatusResponse> GetStatusAsync(string id, int? skip, CancellationToken cancellationToken)
    {
        var path = $"crawl/{Uri.EscapeDataString(id)}";
        if (skip is not null)
        {
            path += $"?skip={skip}";
        }

        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var status = await response.Content.ReadFromJsonAsync<CrawlStatusResponse>(SerializerOptions, cancellationToken);
        return status ?? new CrawlStatusResponse { Status = "failed" };
    }

    private static int? TryParseSkip(string nextUrl)
    {
        var queryIndex = nextUrl.IndexOf('?');
        if (queryIndex < 0)
        {
            return null;
        }

        foreach (var pair in nextUrl.Substring(queryIndex + 1).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("skip", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(kv[1], out var skip))
            {
                return skip;
            }
        }
        return null;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? message = null;
        string? code = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<FirecrawlErrorResponse>(SerializerOptions, cancellationToken);
            message = error?.Error;
            code = error?.Code;
        }
        catch (JsonException)
        {
            // Body was not the documented error shape; fall back to the raw text below.
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            message = await SafeReadBodyAsync(response, cancellationToken);
        }

        throw new FirecrawlApiException(
            response.StatusCode,
            code,
            $"Firecrawl request failed ({(int)response.StatusCode} {response.StatusCode}): {message}");
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(text) ? response.ReasonPhrase ?? "Unknown error" : text;
        }
        catch
        {
            return response.ReasonPhrase ?? "Unknown error";
        }
    }
}
