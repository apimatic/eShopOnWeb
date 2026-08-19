using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written client for Twilio's Messages API, built to the <c>twilio_api_v2010</c>
/// OpenAPI contract. Sends, reads, cancels, redacts and lists messages. All calls use the
/// configured messaging base URL and HTTP Basic auth. This type performs no logging, so
/// phone numbers and message bodies never reach a log sink through it.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private readonly HttpClient _http;
    private readonly TwilioOptions _options;

    public TwilioMessagingClient(HttpClient http, IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        _http = http;
        _http.BaseAddress ??= new Uri(_options.MessagingBaseUrl + "/");
        _http.DefaultRequestHeaders.Authorization = BasicAuthHeader(_options);
    }

    internal static AuthenticationHeaderValue BasicAuthHeader(TwilioOptions options)
    {
        var raw = Encoding.UTF8.GetBytes($"{options.AccountSid}:{options.AuthToken}");
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }

    private string MessagesResource => $"2010-04-01/Accounts/{_options.AccountSid}/Messages.json";
    private string MessageResource(string sid) => $"2010-04-01/Accounts/{_options.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    public async Task<TwilioMessage> SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["To"] = command.To, ["Body"] = command.Body };

        if (!string.IsNullOrWhiteSpace(command.MessagingServiceSid))
            form["MessagingServiceSid"] = command.MessagingServiceSid!;
        if (!string.IsNullOrWhiteSpace(command.From))
            form["From"] = command.From!;
        if (!string.IsNullOrWhiteSpace(command.ScheduleType))
            form["ScheduleType"] = command.ScheduleType!;
        if (command.SendAt.HasValue)
            form["SendAt"] = command.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(MessagesResource, content, cancellationToken);
        return await TwilioResponseReader.ReadMessageAsync(response, cancellationToken);
    }

    public async Task<TwilioMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(MessageResource(messageSid), cancellationToken);
        return await TwilioResponseReader.ReadMessageAsync(response, cancellationToken);
    }

    public async Task<TwilioMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "canceled" });
        using var response = await _http.PostAsync(MessageResource(messageSid), content, cancellationToken);
        return await TwilioResponseReader.ReadMessageAsync(response, cancellationToken);
    }

    public async Task<TwilioMessage> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Per the spec, an empty Body redacts the message's text at the provider.
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Body"] = string.Empty });
        using var response = await _http.PostAsync(MessageResource(messageSid), content, cancellationToken);
        return await TwilioResponseReader.ReadMessageAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(TwilioMessageListQuery query, CancellationToken cancellationToken = default)
    {
        var results = new List<TwilioMessage>();

        // First page: build the query string ourselves so the ">=" / "<=" date operators
        // (Twilio's DateSent> / DateSent< parameters) are expressed correctly.
        string? relativeUri = BuildFirstPageUri(query);

        // Guard against an unbounded follow-the-cursor loop.
        for (var page = 0; relativeUri is not null && page < 1000; page++)
        {
            using var response = await _http.GetAsync(relativeUri, cancellationToken);
            await TwilioResponseReader.EnsureSuccessAsync(response, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                    results.Add(TwilioResponseReader.MapMessage(m));
            }

            relativeUri = root.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }

        return results;
    }

    private string BuildFirstPageUri(TwilioMessageListQuery query)
    {
        var parts = new List<string>();
        // Escape the key too: Twilio's operator params must arrive as "DateSent%3E" / "DateSent%3C".
        void Add(string key, string value) => parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");

        if (!string.IsNullOrWhiteSpace(query.From)) Add("From", query.From!);
        if (!string.IsNullOrWhiteSpace(query.To)) Add("To", query.To!);
        // Twilio's "DateSent>" means on-or-after; "DateSent<" means on-or-before. A bare
        // YYYY-MM-DD upper bound is treated as that day's 00:00, which would exclude the whole
        // day, so we send full UTC date-times (an accepted format) to cover the range exactly.
        if (query.DateSentAfter.HasValue)
            Add("DateSent>", query.DateSentAfter.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        if (query.DateSentBefore.HasValue)
            Add("DateSent<", query.DateSentBefore.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        Add("PageSize", query.PageSize.ToString(CultureInfo.InvariantCulture));

        return MessagesResource + "?" + string.Join("&", parts);
    }
}
