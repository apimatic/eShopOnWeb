using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class TwilioMessagingClient : ITwilioMessagingClient, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TwilioMessagingClient(IOptions<TwilioOptions> options)
        : this(options, new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = DecompressionMethods.All
        })
    {
    }

    public TwilioMessagingClient(IOptions<TwilioOptions> options, HttpMessageHandler handler)
    {
        _options = options.Value;
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string input,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(input)}";
        using var request = CreateRequest(HttpMethod.Get, LookupBaseUrl + path);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(response, cancellationToken);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<LookupResponse>(stream, _jsonOptions, cancellationToken);
        return new ValidatedPhoneNumber(result?.Valid == true, result?.PhoneNumber);
    }

    public async Task<ProviderMessage> SendAsync(string destination, string body,
        DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration(sendAt.HasValue);
        var values = new List<KeyValuePair<string, string>>
        {
            new("To", destination),
            new("From", _options.FromNumber),
            new("Body", body),
            new("MessagingServiceSid", _options.MessagingServiceSid)
        };

        if (sendAt.HasValue)
        {
            values.Add(new("ScheduleType", "fixed"));
            values.Add(new("SendAt", sendAt.Value.ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
        using var request = CreateRequest(HttpMethod.Post, MessagingUrl(path));
        request.Content = new FormUrlEncodedContent(values);
        using var response = await SendAsync(request, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<ProviderMessage> GetMessageAsync(string providerMessageSid,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration(false);
        var path = MessagePath(providerMessageSid);
        using var request = CreateRequest(HttpMethod.Get, MessagingUrl(path));
        using var response = await SendAsync(request, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<ProviderMessage> CancelScheduledMessageAsync(string providerMessageSid,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration(false);
        using var request = CreateRequest(HttpMethod.Post, MessagingUrl(MessagePath(providerMessageSid)));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "canceled" });
        using var response = await SendAsync(request, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task RedactMessageContentAsync(string providerMessageSid,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration(false);
        using var request = CreateRequest(HttpMethod.Post, MessagingUrl(MessagePath(providerMessageSid)));
        request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Body", string.Empty) });
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(response, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration(false);
        var startDate = from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endDate = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json" +
                   $"?From={Uri.EscapeDataString(_options.FromNumber)}" +
                   $"&DateSent%3E={startDate}&DateSent%3C={endDate}&PageSize=1000";
        var messages = new List<ProviderMessage>();

        while (!string.IsNullOrWhiteSpace(path))
        {
            using var request = CreateRequest(HttpMethod.Get, MessagingUrl(NormalizeProviderPagePath(path)));
            using var response = await SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateProviderExceptionAsync(response, cancellationToken);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var page = await JsonSerializer.DeserializeAsync<MessagePageResponse>(stream, _jsonOptions, cancellationToken)
                ?? new MessagePageResponse();
            messages.AddRange(page.Messages.Select(ToProviderMessage));
            path = page.NextPageUri ?? string.Empty;
        }

        return messages.Where(x => x.DateSent >= from && x.DateSent <= to).ToList();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        EnsureCredentials();
        var request = new HttpRequestMessage(method, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TwilioProviderException((int)HttpStatusCode.GatewayTimeout);
        }
        catch (HttpRequestException)
        {
            throw new TwilioProviderException((int)HttpStatusCode.BadGateway);
        }
    }

    private async Task<ProviderMessage> ReadMessageAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(response, cancellationToken);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var message = await JsonSerializer.DeserializeAsync<MessageResponse>(stream, _jsonOptions, cancellationToken);
        if (message is null || string.IsNullOrWhiteSpace(message.Sid))
        {
            throw new TwilioProviderException((int)HttpStatusCode.BadGateway);
        }

        return ToProviderMessage(message);
    }

    private async Task<TwilioProviderException> CreateProviderExceptionAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        int? code = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var error = await JsonSerializer.DeserializeAsync<ErrorResponse>(stream, _jsonOptions, cancellationToken);
            code = error?.Code;
        }
        catch (JsonException)
        {
            // Deliberately do not retain or log the provider response because it may contain a phone number.
        }

        return new TwilioProviderException((int)response.StatusCode, code);
    }

    private static ProviderMessage ToProviderMessage(MessageResponse message) =>
        new(message.Sid ?? string.Empty, message.Status ?? "unknown", message.ErrorCode,
            ParseDate(message.DateCreated), ParseDate(message.DateSent));

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result)
            ? result.ToUniversalTime()
            : null;

    private string MessagingUrl(string path)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl;
        return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }

    private string MessagePath(string sid) =>
        $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private static string NormalizeProviderPagePath(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return uri.PathAndQuery;
        }

        return value;
    }

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            throw new TwilioProviderException((int)HttpStatusCode.ServiceUnavailable);
        }
    }

    private void EnsureMessagingConfiguration(bool scheduled)
    {
        EnsureCredentials();
        if (string.IsNullOrWhiteSpace(_options.FromNumber) ||
            string.IsNullOrWhiteSpace(_options.MessagingServiceSid) ||
            (scheduled && !_options.MessagingServiceSid.StartsWith("MG", StringComparison.Ordinal)))
        {
            throw new TwilioProviderException((int)HttpStatusCode.ServiceUnavailable);
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")] public string? Sid { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    }

    private sealed class MessagePageResponse
    {
        [JsonPropertyName("messages")] public List<MessageResponse> Messages { get; set; } = new();
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
    }
}
