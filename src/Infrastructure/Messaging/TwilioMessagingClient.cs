using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string MessagingClientName = "TwilioMessaging";
    public const string LookupClientName = "TwilioLookup";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioOptions _options;
    private readonly AuthenticationHeaderValue _authorization;

    public TwilioMessagingClient(IHttpClientFactory httpClientFactory, IOptions<TwilioOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        var credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _authorization = new AuthenticationHeaderValue("Basic", credential);
    }

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(
        string phoneNumber,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            path += $"?CountryCode={Uri.EscapeDataString(countryCode.Trim().ToUpperInvariant())}";
        }

        var response = await SendAsync(LookupClientName, HttpMethod.Get, path, null, "phone-number validation", cancellationToken);
        var dto = await DeserializeAsync<LookupResponse>(response, "phone-number validation", cancellationToken);
        return new PhoneNumberValidation(
            dto.Valid && !string.IsNullOrWhiteSpace(dto.PhoneNumber),
            dto.PhoneNumber,
            dto.ValidationErrors ?? Array.Empty<string>());
    }

    public Task<ProviderMessage> SendMessageAsync(
        string to,
        string content,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.FromNumber),
            new("Body", content),
            new("MessagingServiceSid", _options.MessagingServiceSid)
        };

        if (sendAt is not null)
        {
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        return SendMessageRequestAsync(HttpMethod.Post, MessagesPath(), form, "message creation", cancellationToken);
    }

    public Task<ProviderMessage> FetchMessageAsync(string providerMessageId, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(HttpMethod.Get, MessagePath(providerMessageId), null, "message fetch", cancellationToken);

    public Task<ProviderMessage> CancelMessageAsync(string providerMessageId, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(
            HttpMethod.Post,
            MessagePath(providerMessageId),
            new[] { new KeyValuePair<string, string>("Status", "canceled") },
            "scheduled-message cancellation",
            cancellationToken);

    public Task<ProviderMessage> RedactMessageAsync(string providerMessageId, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(
            HttpMethod.Post,
            MessagePath(providerMessageId),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) },
            "message redaction",
            cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        // Twilio's list filters have day precision. Ask for a safely enclosing UTC window,
        // then enforce the caller's exact instants below.
        var after = from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var before = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var path = MessagesPath() + "?" + string.Join("&", new[]
        {
            Pair("From", _options.FromNumber),
            Pair("DateSent>", after),
            Pair("DateSent<", before),
            Pair("PageSize", "1000")
        });

        var messages = new List<ProviderMessage>();
        string? next = path;
        while (next is not null)
        {
            var response = await SendAsync(MessagingClientName, HttpMethod.Get, AsMessagingRelativeUri(next), null, "message listing", cancellationToken);
            var page = await DeserializeAsync<MessageListResponse>(response, "message listing", cancellationToken);
            messages.AddRange((page.Messages ?? Array.Empty<MessageResponse>())
                .Select(ToProviderMessage)
                .Where(x => x.DateSent is not null && x.DateSent >= from && x.DateSent <= to));
            next = page.NextPageUri;
        }

        return messages;
    }

    private async Task<ProviderMessage> SendMessageRequestAsync(
        HttpMethod method,
        string path,
        IEnumerable<KeyValuePair<string, string>>? form,
        string operation,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var response = await SendAsync(MessagingClientName, method, path, form, operation, cancellationToken);
        return ToProviderMessage(await DeserializeAsync<MessageResponse>(response, operation, cancellationToken));
    }

    private async Task<HttpResponseMessage> SendAsync(
        string clientName,
        HttpMethod method,
        string path,
        IEnumerable<KeyValuePair<string, string>>? form,
        string operation,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = _authorization;
        if (form is not null) request.Content = new FormUrlEncodedContent(form);

        HttpResponseMessage response;
        try
        {
            response = await _httpClientFactory.CreateClient(clientName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new TwilioProviderException(operation);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TwilioProviderException(operation);
        }
        if (response.IsSuccessStatusCode) return response;

        int? errorCode = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TwilioError>(JsonOptions, cancellationToken);
            errorCode = error?.Code;
        }
        catch (JsonException)
        {
            // The exception deliberately excludes response content because it can contain PII.
        }

        var statusCode = (int)response.StatusCode;
        response.Dispose();
        throw new TwilioProviderException(operation, statusCode, errorCode);
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        using (response)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return value ?? throw new TwilioProviderException(operation, (int)response.StatusCode);
        }
    }

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
            throw new TwilioProviderException("authentication configuration");
    }

    private string MessagesPath() => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
    private string MessagePath(string sid) => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";
    private static string Pair(string name, string value) => $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";

    private static string AsMessagingRelativeUri(string nextPageUri)
    {
        if (!Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute)) return nextPageUri.TrimStart('/');
        return (absolute.AbsolutePath + absolute.Query).TrimStart('/');
    }

    private static ProviderMessage ToProviderMessage(MessageResponse response) => new(
        response.Sid ?? string.Empty,
        response.Status ?? "unknown",
        ParseDate(response.DateCreated) ?? DateTimeOffset.UtcNow,
        ParseDate(response.DateSent),
        response.ErrorCode,
        response.ErrorMessage);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }
        public bool Valid { get; set; }
        [JsonPropertyName("validation_errors")]
        public string[]? ValidationErrors { get; set; }
    }

    private sealed class MessageResponse
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }
        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }
        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }
    }

    private sealed class MessageListResponse
    {
        public MessageResponse[]? Messages { get; set; }
        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioError
    {
        public int? Code { get; set; }
    }
}
