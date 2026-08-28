using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class TwilioMessageProvider : IMessageProvider, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly string _messagingBaseUrl;

    public TwilioMessageProvider(IOptions<TwilioOptions> options)
        : this(options.Value, new HttpClient())
    {
    }

    internal TwilioMessageProvider(TwilioOptions options, HttpClient httpClient)
    {
        _options = options;
        _httpClient = httpClient;
        _messagingBaseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : options.BaseUrl;

        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber,
        string? countryCode, CancellationToken cancellationToken = default)
    {
        EnsureCredentials();
        var uri = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            uri += $"?CountryCode={Uri.EscapeDataString(countryCode.ToUpperInvariant())}";
        }

        using var response = await SendHttpAsync(new HttpRequestMessage(HttpMethod.Get, uri),
            "phone-number validation", cancellationToken);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var root = document.RootElement;
        var valid = root.TryGetProperty("valid", out var validElement) && validElement.GetBoolean();
        var canonical = root.TryGetProperty("phone_number", out var numberElement)
            ? numberElement.GetString()
            : null;
        var errors = root.TryGetProperty("validation_errors", out var errorsElement)
            && errorsElement.ValueKind == JsonValueKind.Array
            ? errorsElement.EnumerateArray().Select(x => x.GetString() ?? "INVALID").ToArray()
            : Array.Empty<string>();
        return new PhoneNumberValidation(valid, canonical, errors);
    }

    public async Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMessagingConfiguration(sendAt.HasValue);
        var values = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.FromNumber),
            new("Body", body)
        };
        if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            values.Add(new("MessagingServiceSid", _options.MessagingServiceSid));
        }
        if (sendAt.HasValue)
        {
            values.Add(new("ScheduleType", "fixed"));
            values.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagingUri(MessagesPath()))
        {
            Content = new FormUrlEncodedContent(values)
        };
        using var response = await SendHttpAsync(request, "message creation", cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public Task<ProviderMessage> GetAsync(string providerMessageId,
        CancellationToken cancellationToken = default) =>
        SendMessageRequestAsync(HttpMethod.Get, MessagePath(providerMessageId), null,
            "message retrieval", cancellationToken);

    public Task<ProviderMessage> CancelAsync(string providerMessageId,
        CancellationToken cancellationToken = default) =>
        SendMessageRequestAsync(HttpMethod.Post, MessagePath(providerMessageId),
            new[] { new KeyValuePair<string, string>("Status", "canceled") },
            "scheduled-message cancellation", cancellationToken);

    public Task<ProviderMessage> RedactAsync(string providerMessageId,
        CancellationToken cancellationToken = default) =>
        SendMessageRequestAsync(HttpMethod.Post, MessagePath(providerMessageId),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) },
            "message redaction", cancellationToken);

    public async Task<IReadOnlyCollection<ProviderMessage>> ListApplicationMessagesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        EnsureMessagingConfiguration(false);
        var lowerDate = from.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var upperDate = to.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dateFilter = lowerDate == upperDate
            ? $"&DateSent={lowerDate}"
            : $"&DateSent%3E={lowerDate}&DateSent%3C={upperDate}";
        var path = $"{MessagesPath()}?From={Uri.EscapeDataString(_options.FromNumber)}" +
                   $"{dateFilter}&PageSize=1000";
        var messages = new List<ProviderMessage>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (!string.IsNullOrWhiteSpace(path) && visited.Add(path))
        {
            using var response = await SendHttpAsync(
                new HttpRequestMessage(HttpMethod.Get, MessagingUri(path)),
                "message reconciliation", cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            foreach (var element in document.RootElement.GetProperty("messages").EnumerateArray())
            {
                var message = ReadMessage(element);
                var effectiveDate = message.DateSent ?? message.DateCreated;
                if (effectiveDate >= from && effectiveDate <= to) messages.Add(message);
            }

            path = document.RootElement.TryGetProperty("next_page_uri", out var next)
                && next.ValueKind == JsonValueKind.String
                ? NormalizeNextPage(next.GetString())
                : null;
        }

        return messages;
    }

    private async Task<ProviderMessage> SendMessageRequestAsync(HttpMethod method, string path,
        IEnumerable<KeyValuePair<string, string>>? form, string operation,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration(false);
        using var request = new HttpRequestMessage(method, MessagingUri(path));
        if (form is not null) request.Content = new FormUrlEncodedContent(form);
        using var response = await SendHttpAsync(request, operation, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendHttpAsync(HttpRequestMessage request, string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return response;

            var errorCode = await TryReadErrorCodeAsync(response, cancellationToken);
            response.Dispose();
            throw new MessageProviderException(operation, errorCode);
        }
        catch (MessageProviderException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new MessageProviderException(operation, innerException: exception);
        }
    }

    private static async Task<int?> TryReadErrorCodeAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<ProviderMessage> ReadMessageAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return ReadMessage(document.RootElement);
    }

    private static ProviderMessage ReadMessage(JsonElement element)
    {
        return new ProviderMessage(
            element.GetProperty("sid").GetString() ?? string.Empty,
            element.GetProperty("status").GetString() ?? "unknown",
            element.TryGetProperty("error_code", out var errorCode)
                && errorCode.ValueKind == JsonValueKind.Number && errorCode.TryGetInt32(out var code)
                ? code
                : null,
            ReadDate(element, "date_created"),
            ReadDate(element, "date_sent"));
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private string MessagesPath() =>
        $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessagePath(string sid) =>
        $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private Uri MessagingUri(string path) =>
        new($"{_messagingBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}", UriKind.Absolute);

    private static string? NormalizeNextPage(string? nextPage)
    {
        if (string.IsNullOrWhiteSpace(nextPage)) return null;
        return Uri.TryCreate(nextPage, UriKind.Absolute, out var absolute)
            ? absolute.PathAndQuery
            : nextPage;
    }

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
            throw new MessageProviderException("configuration");
    }

    private void EnsureMessagingConfiguration(bool scheduled)
    {
        EnsureCredentials();
        if (string.IsNullOrWhiteSpace(_options.FromNumber)
            || (scheduled && string.IsNullOrWhiteSpace(_options.MessagingServiceSid)))
            throw new MessageProviderException("configuration");
    }

    public void Dispose() => _httpClient.Dispose();
}
