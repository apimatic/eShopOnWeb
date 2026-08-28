using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioMessagingClient : ITextMessagingProvider
{
    private const string DefaultBaseUrl = "https://api.twilio.com";
    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly string _baseUrl;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultBaseUrl : _options.BaseUrl!;
    }

    public Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", body)
        };
        if (sendAt.HasValue)
        {
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        return SendFormAsync(HttpMethod.Post, MessagesPath(), fields, cancellationToken);
    }

    public Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default) =>
        SendConfiguredAsync(HttpMethod.Get, MessagePath(providerMessageSid), cancellationToken);

    public Task<ProviderMessage> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default) =>
        SendConfiguredFormAsync(HttpMethod.Post, MessagePath(providerMessageSid),
            new[] { new KeyValuePair<string, string>("Status", "canceled") }, cancellationToken);

    public Task<ProviderMessage> RedactAsync(string providerMessageSid, CancellationToken cancellationToken = default) =>
        SendConfiguredFormAsync(HttpMethod.Post, MessagePath(providerMessageSid),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) }, cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        // Twilio's contract exposes date (not instant) filters. Query a covering set, then
        // trim to the requested ISO-8601 instants after every provider page has been read.
        var fromDate = from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var query = $"?From={Uri.EscapeDataString(_options.FromNumber)}" +
                    $"&DateSent%3E={Uri.EscapeDataString(fromDate)}" +
                    $"&DateSent%3C={Uri.EscapeDataString(toDate)}&PageSize=1000";
        string? nextPath = MessagesPath() + query;
        var messages = new List<ProviderMessage>();

        while (!string.IsNullOrEmpty(nextPath))
        {
            using var request = CreateRequest(HttpMethod.Get, NormalizePagePath(nextPath));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await TwilioHttp.EnsureSuccessAsync(response, cancellationToken);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var root = document.RootElement;
            if (root.TryGetProperty("messages", out var items))
            {
                messages.AddRange(items.EnumerateArray().Select(ParseMessage));
            }
            nextPath = root.TryGetProperty("next_page_uri", out var next) && next.ValueKind != JsonValueKind.Null
                ? next.GetString()
                : null;
        }

        return messages.Where(x =>
        {
            var timestamp = x.DateSent ?? x.DateCreated;
            return timestamp >= from && timestamp <= to;
        }).ToList();
    }

    private async Task<ProviderMessage> SendAsync(HttpMethod method, string path,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await TwilioHttp.EnsureSuccessAsync(response, cancellationToken);
        return await ParseMessageAsync(response, cancellationToken);
    }

    private Task<ProviderMessage> SendConfiguredAsync(HttpMethod method, string path,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return SendAsync(method, path, cancellationToken);
    }

    private Task<ProviderMessage> SendConfiguredFormAsync(HttpMethod method, string path,
        IEnumerable<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return SendFormAsync(method, path, fields, cancellationToken);
    }

    private async Task<ProviderMessage> SendFormAsync(HttpMethod method, string path,
        IEnumerable<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path);
        request.Content = new FormUrlEncodedContent(fields);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await TwilioHttp.EnsureSuccessAsync(response, cancellationToken);
        return await ParseMessageAsync(response, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri($"{_baseUrl.TrimEnd('/')}/{path.TrimStart('/')}"));
        TwilioHttp.ApplyBasicAuthentication(request, _options);
        return request;
    }

    private async Task<ProviderMessage> ParseMessageAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        return ParseMessage(document.RootElement);
    }

    private static ProviderMessage ParseMessage(JsonElement root) => new(
        RequiredString(root, "sid"),
        OptionalString(root, "from"),
        OptionalString(root, "to"),
        OptionalString(root, "body"),
        OptionalString(root, "status") ?? "unknown",
        root.TryGetProperty("error_code", out var code) && code.ValueKind == JsonValueKind.Number ? code.GetInt32() : null,
        OptionalString(root, "error_message"),
        TwilioHttp.ParseDate(root, "date_created"),
        TwilioHttp.ParseDate(root, "date_sent"));

    private static string RequiredString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } result
            ? result
            : throw new MessagingProviderException($"Twilio response omitted required field '{name}'.");

    private static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private string MessagesPath() => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
    private string MessagePath(string sid) => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private static string NormalizePagePath(string nextPage)
    {
        return Uri.TryCreate(nextPage, UriKind.Absolute, out var absolute)
            ? absolute.PathAndQuery
            : nextPage;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken) ||
            string.IsNullOrWhiteSpace(_options.FromNumber) || string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            throw new MessagingProviderException("Twilio messaging settings are incomplete.");
        }
    }
}
