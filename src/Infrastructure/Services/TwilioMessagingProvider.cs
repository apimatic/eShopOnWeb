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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class TwilioMessagingProvider : IOrderMessagingProvider, IDisposable
{
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;

    public TwilioMessagingProvider(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!string.IsNullOrWhiteSpace(_options.AccountSid) && !string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        EnsureConfigured(requireMessagingService: false);
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new PhoneNumberValidation(false, null);
        }

        var encodedNumber = Uri.EscapeDataString(phoneNumber.Trim());
        var uri = new Uri($"{LookupBaseUrl}/v2/PhoneNumbers/{encodedNumber}");
        using var response = await SendRequestAsync(HttpMethod.Get, uri, null, "phone-number validation", cancellationToken);
        var model = await DeserializeAsync<LookupResponse>(response, "phone-number validation", cancellationToken);
        return new PhoneNumberValidation(model.Valid && !string.IsNullOrWhiteSpace(model.PhoneNumber), model.PhoneNumber);
    }

    public Task<ProviderMessageState> SendAsync(string to, string body, CancellationToken cancellationToken)
    {
        var values = MessageValues(to, body);
        return CreateMessageAsync(values, "send", cancellationToken);
    }

    public Task<ProviderMessageState> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var values = MessageValues(to, body);
        values.Add(new("ScheduleType", "fixed"));
        values.Add(new("SendAt", sendAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
        return CreateMessageAsync(values, "schedule", cancellationToken);
    }

    public async Task<ProviderMessageState> GetAsync(string messageSid, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await SendRequestAsync(
            HttpMethod.Get,
            MessagingUri(MessagePath(messageSid)),
            null,
            "status lookup",
            cancellationToken);
        return ToState(await DeserializeAsync<MessageResponse>(response, "status lookup", cancellationToken));
    }

    public Task<ProviderMessageState> CancelAsync(string messageSid, CancellationToken cancellationToken) =>
        UpdateMessageAsync(messageSid, new[] { new KeyValuePair<string, string>("Status", "canceled") }, "cancellation", cancellationToken);

    public Task<ProviderMessageState> RedactContentAsync(string messageSid, CancellationToken cancellationToken) =>
        UpdateMessageAsync(messageSid, new[] { new KeyValuePair<string, string>("Body", string.Empty) }, "content disposal", cancellationToken);

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListFromApplicationNumberAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var query = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("From", _options.FromNumber),
            new KeyValuePair<string, string>("PageSize", "1000")
        });
        var queryString = await query.ReadAsStringAsync(cancellationToken);
        var nextPath = $"{MessagesPath()}?{queryString}";
        var records = new List<ProviderMessageRecord>();

        while (!string.IsNullOrWhiteSpace(nextPath))
        {
            using var response = await SendRequestAsync(
                HttpMethod.Get,
                MessagingUri(NormalizeNextPagePath(nextPath)),
                null,
                "reconciliation listing",
                cancellationToken);
            var page = await DeserializeAsync<MessageListResponse>(response, "reconciliation listing", cancellationToken);
            records.AddRange(page.Messages.Select(ToRecord));
            nextPath = page.NextPageUri;
        }

        return records;
    }

    private async Task<ProviderMessageState> CreateMessageAsync(
        IEnumerable<KeyValuePair<string, string>> values,
        string operation,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var content = new FormUrlEncodedContent(values);
        using var response = await SendRequestAsync(
            HttpMethod.Post,
            MessagingUri(MessagesPath()),
            content,
            operation,
            cancellationToken);
        return ToState(await DeserializeAsync<MessageResponse>(response, operation, cancellationToken));
    }

    private async Task<ProviderMessageState> UpdateMessageAsync(
        string messageSid,
        IEnumerable<KeyValuePair<string, string>> values,
        string operation,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var content = new FormUrlEncodedContent(values);
        using var response = await SendRequestAsync(
            HttpMethod.Post,
            MessagingUri(MessagePath(messageSid)),
            content,
            operation,
            cancellationToken);
        return ToState(await DeserializeAsync<MessageResponse>(response, operation, cancellationToken));
    }

    private List<KeyValuePair<string, string>> MessageValues(string to, string body) => new()
    {
        new("To", to),
        new("From", _options.FromNumber),
        new("MessagingServiceSid", _options.MessagingServiceSid),
        new("Body", body)
    };

    private async Task<HttpResponseMessage> SendRequestAsync(
        HttpMethod method,
        Uri uri,
        HttpContent? content,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri) { Content = content };
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            response.Dispose();
            throw new MessagingProviderException(operation);
        }
        catch (MessagingProviderException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new MessagingProviderException(operation);
        }
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                ?? throw new MessagingProviderException(operation);
        }
        catch (MessagingProviderException)
        {
            throw;
        }
        catch
        {
            throw new MessagingProviderException(operation);
        }
    }

    private void EnsureConfigured(bool requireMessagingService = true)
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken) ||
            (requireMessagingService && (string.IsNullOrWhiteSpace(_options.FromNumber) || string.IsNullOrWhiteSpace(_options.MessagingServiceSid))))
        {
            throw new MessagingProviderException("configuration");
        }
    }

    private Uri MessagingUri(string path)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? TwilioOptions.DefaultMessagingBaseUrl
            : _options.BaseUrl;
        return new Uri($"{baseUrl!.TrimEnd('/')}/{path.TrimStart('/')}", UriKind.Absolute);
    }

    private string MessagesPath() => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
    private string MessagePath(string sid) => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private static string NormalizeNextPagePath(string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery;
        }

        return nextPageUri;
    }

    private static ProviderMessageState ToState(MessageResponse response) =>
        new(response.Sid, response.Status, response.ErrorCode, response.ErrorMessage);

    private static ProviderMessageRecord ToRecord(MessageResponse response) =>
        new(
            response.Sid,
            response.Status,
            response.From,
            response.To,
            ParseRequiredDate(response.DateCreated),
            ParseOptionalDate(response.DateSent),
            response.ErrorCode,
            response.ErrorMessage);

    private static DateTimeOffset ParseRequiredDate(string? value) =>
        ParseOptionalDate(value) ?? DateTimeOffset.MinValue;

    private static DateTimeOffset? ParseOptionalDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    public void Dispose() => _httpClient.Dispose();

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<MessageResponse> Messages { get; set; } = new();

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")]
        public string Sid { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }
    }
}
