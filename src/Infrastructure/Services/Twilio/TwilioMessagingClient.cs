using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient http, IOptions<TwilioSettings> options, ILogger<TwilioMessagingClient> logger)
    {
        _http = http;
        _settings = options.Value;
        _logger = logger;
        _http.DefaultRequestHeaders.Authorization = TwilioHttp.BasicAuth(_settings);
        _http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string GetConfiguredFromNumber() => _settings.FromNumber;

    public Task<ProviderMessage> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default)
    {
        var path = MessagesCollectionPath();
        return SendFormAsync(
            path,
            () =>
            {
                var fields = new List<KeyValuePair<string, string>>
                {
                    new("To", request.To),
                    new("Body", request.Body),
                    new("SmartEncoded", "true")
                };

                if (!string.IsNullOrWhiteSpace(_settings.FromNumber))
                {
                    fields.Add(new("From", _settings.FromNumber));
                }

                if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
                {
                    fields.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
                }

                if (request.SendAt.HasValue)
                {
                    fields.Add(new("ScheduleType", "fixed"));
                    fields.Add(new("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")));
                }

                return fields;
            },
            retryServerErrors: false,
            cancellationToken);
    }

    public Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
        => SendWithoutBodyAsync(HttpMethod.Get, MessageInstancePath(messageSid), retryServerErrors: true, cancellationToken);

    public Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
        => SendFormAsync(
            MessageInstancePath(messageSid),
            () => new List<KeyValuePair<string, string>> { new("Body", string.Empty) },
            retryServerErrors: true,
            cancellationToken);

    public Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
        => SendFormAsync(
            MessageInstancePath(messageSid),
            () => new List<KeyValuePair<string, string>> { new("Status", "canceled") },
            retryServerErrors: true,
            cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            "PageSize=1000",
            "From=" + Uri.EscapeDataString(fromNumber),
            "DateSent%3E=" + Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            "DateSent%3C=" + Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"))
        };

        var next = MessagesCollectionPath() + "?" + string.Join("&", query);
        var results = new List<ProviderMessage>();

        while (!string.IsNullOrEmpty(next))
        {
            var url = TwilioHttp.ResolveMessagingUri(_settings, next);
            using var response = await TwilioRequestSender.SendAsync(
                _http,
                () => new HttpRequestMessage(HttpMethod.Get, url),
                retryServerErrors: true,
                _logger,
                cancellationToken);

            await TwilioRequestSender.EnsureSuccessAsync(response, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var page = JsonSerializer.Deserialize<TwilioMessageListDto>(payload, TwilioRequestSender.JsonOptions)
                       ?? new TwilioMessageListDto();

            foreach (var message in page.Messages)
            {
                results.Add(ToProviderMessage(message));
            }

            next = string.IsNullOrEmpty(page.NextPageUri) ? null : page.NextPageUri;
        }

        return results;
    }

    private string MessagesCollectionPath()
        => $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstancePath(string sid)
        => $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    private async Task<ProviderMessage> SendFormAsync(
        string path,
        Func<List<KeyValuePair<string, string>>> fields,
        bool retryServerErrors,
        CancellationToken cancellationToken)
    {
        var url = TwilioHttp.ResolveMessagingUri(_settings, path);
        using var response = await TwilioRequestSender.SendAsync(
            _http,
            () => new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(fields())
            },
            retryServerErrors,
            _logger,
            cancellationToken);

        await TwilioRequestSender.EnsureSuccessAsync(response, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<ProviderMessage> SendWithoutBodyAsync(
        HttpMethod method,
        string path,
        bool retryServerErrors,
        CancellationToken cancellationToken)
    {
        var url = TwilioHttp.ResolveMessagingUri(_settings, path);
        using var response = await TwilioRequestSender.SendAsync(
            _http,
            () => new HttpRequestMessage(method, url),
            retryServerErrors,
            _logger,
            cancellationToken);

        await TwilioRequestSender.EnsureSuccessAsync(response, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private static async Task<ProviderMessage> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var dto = JsonSerializer.Deserialize<TwilioMessageDto>(payload, TwilioRequestSender.JsonOptions)
                  ?? new TwilioMessageDto();
        return ToProviderMessage(dto);
    }

    private static ProviderMessage ToProviderMessage(TwilioMessageDto dto)
    {
        return new ProviderMessage(
            dto.Sid ?? string.Empty,
            dto.Status ?? string.Empty,
            dto.Body,
            dto.From,
            dto.To,
            ParseTwilioDate(dto.DateSent),
            ParseTwilioDate(dto.DateCreated),
            dto.ErrorCode,
            dto.ErrorMessage);
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }
}
