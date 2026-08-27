using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class TwilioMessagingClient : ITwilioMessagingClient, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioMessagingClient(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneValidationResult> ValidatePhoneNumberAsync(string number, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var uri = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(number)}";
        using var response = await _httpClient.GetAsync(uri, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode is 400 or 404)
            {
                return new PhoneValidationResult(false, null);
            }

            throw await CreateProviderExceptionAsync(response, cancellationToken);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<LookupResponse>(stream, _jsonOptions, cancellationToken);
        return new PhoneValidationResult(result?.Valid == true, result?.PhoneNumber);
    }

    public async Task<ProviderMessage> SendAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var values = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _options.FromNumber,
            ["Body"] = body
        };

        if (sendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            {
                throw new TwilioProviderException(500, null);
            }

            values["MessagingServiceSid"] = _options.MessagingServiceSid;
            values["ScheduleType"] = "fixed";
            values["SendAt"] = sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        using var response = await _httpClient.PostAsync(MessagesUri(), new FormUrlEncodedContent(values), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<ProviderMessage> GetAsync(string messageSid, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        using var response = await _httpClient.GetAsync(MessageUri(messageSid), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken)
    {
        return UpdateAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);
    }

    public async Task RedactContentAsync(string messageSid, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (attempt % 5 == 0)
            {
                try
                {
                    // Twilio may temporarily return 20409 while the Message is finalizing,
                    // and an accepted redaction can itself complete asynchronously.
                    var updated = await UpdateAsync(
                        messageSid,
                        new Dictionary<string, string> { ["Body"] = string.Empty },
                        cancellationToken);
                    if (string.IsNullOrEmpty(updated.Body))
                    {
                        return;
                    }
                }
                catch (TwilioProviderException ex) when (
                    ex.StatusCode is 409 or 429 || ex.StatusCode >= 500)
                {
                    // Retry only provider states documented as temporary or unavailable.
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            try
            {
                var fetched = await GetAsync(messageSid, cancellationToken);
                if (string.IsNullOrEmpty(fetched.Body))
                {
                    return;
                }
            }
            catch (TwilioProviderException ex) when (ex.StatusCode == 429 || ex.StatusCode >= 500)
            {
                // A transient read must not cause the application to erase its copy early.
            }
        }

        throw new TwilioProviderException(504, null);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var messages = new List<ProviderMessage>();
        var nextUri = $"{MessagesUri()}?From={Uri.EscapeDataString(_options.FromNumber)}&PageSize=1000";

        while (!string.IsNullOrWhiteSpace(nextUri))
        {
            using var response = await _httpClient.GetAsync(nextUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateProviderExceptionAsync(response, cancellationToken);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var page = await JsonSerializer.DeserializeAsync<MessageListResponse>(stream, _jsonOptions, cancellationToken)
                ?? throw new TwilioProviderException(502, null);

            messages.AddRange(page.Messages.Select(Map));
            nextUri = string.IsNullOrWhiteSpace(page.NextPageUri) ? null : MessagingUri(page.NextPageUri);
        }

        return messages;
    }

    private async Task<ProviderMessage> UpdateAsync(
        string messageSid,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        using var response = await _httpClient.PostAsync(MessageUri(messageSid), new FormUrlEncodedContent(values), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<ProviderMessage> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateProviderExceptionAsync(response, cancellationToken);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var message = await JsonSerializer.DeserializeAsync<MessageResponse>(stream, _jsonOptions, cancellationToken)
                ?? throw new TwilioProviderException(502, null);
            return Map(message);
        }
    }

    private async Task<TwilioProviderException> CreateProviderExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        int? code = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var error = await JsonSerializer.DeserializeAsync<TwilioErrorResponse>(stream, _jsonOptions, cancellationToken);
            code = error?.Code;
        }
        catch (JsonException)
        {
            // Deliberately discard provider response text: it can contain a phone number.
        }

        return new TwilioProviderException((int)response.StatusCode, code);
    }

    private string MessagesUri() => MessagingUri($"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json");

    private string MessageUri(string messageSid) => MessagingUri(
        $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json");

    private string MessagingUri(string pathOrUri)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultMessagingBaseUrl : _options.BaseUrl;
        if (Uri.TryCreate(pathOrUri, UriKind.Absolute, out var absolute))
        {
            pathOrUri = absolute.PathAndQuery;
        }

        return $"{baseUrl!.TrimEnd('/')}/{pathOrUri.TrimStart('/')}";
    }

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) ||
            string.IsNullOrWhiteSpace(_options.AuthToken) ||
            string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            throw new TwilioProviderException(500, null);
        }
    }

    private static ProviderMessage Map(MessageResponse message) => new(
        message.Sid,
        message.Status,
        message.From,
        message.To,
        message.Body,
        ParseDate(message.DateCreated) ?? DateTimeOffset.MinValue,
        ParseDate(message.DateSent),
        ParseDate(message.DateUpdated) ?? DateTimeOffset.MinValue,
        message.ErrorCode);

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces);
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }
    }

    private sealed class MessageResponse
    {
        public string Sid { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Body { get; set; }
        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }
        [JsonPropertyName("date_updated")]
        public string? DateUpdated { get; set; }
        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }
    }

    private sealed class MessageListResponse
    {
        public List<MessageResponse> Messages { get; set; } = new();
        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorResponse
    {
        public int? Code { get; set; }
    }
}
