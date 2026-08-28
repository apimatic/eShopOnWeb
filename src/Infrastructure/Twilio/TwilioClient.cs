using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioClient : ITwilioClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var uri = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}?Fields=validation";
        using var request = CreateRequest(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidation(false, null);
        }
        using var json = await ReadResponseAsync(response, cancellationToken);
        var root = json.RootElement;

        var valid = root.TryGetProperty("valid", out var validElement) && validElement.GetBoolean();
        var canonical = root.TryGetProperty("phone_number", out var phoneElement)
            ? phoneElement.GetString()
            : null;
        return new PhoneNumberValidation(valid && !string.IsNullOrWhiteSpace(canonical), canonical);
    }

    public Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration(sendAt.HasValue);
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _settings.FromNumber),
            new("Body", body)
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        if (sendAt.HasValue)
        {
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        return SendMessageRequestAsync(HttpMethod.Post, MessagesPath(), form, cancellationToken);
    }

    public Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration(false);
        return SendMessageRequestAsync(HttpMethod.Get, MessagePath(messageSid), null, cancellationToken);
    }

    public Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration(false);
        return SendMessageRequestAsync(HttpMethod.Post, MessagePath(messageSid),
            new[] { new KeyValuePair<string, string>("Status", "canceled") }, cancellationToken);
    }

    public async Task<ProviderMessage> RedactMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration(false);
        await SendMessageRequestAsync(HttpMethod.Post, MessagePath(messageSid),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) }, cancellationToken);
        return await FetchMessageAsync(messageSid, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration(false);
        var fromDate = from.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var throughDate = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var query = EncodeQuery(new[]
        {
            new KeyValuePair<string, string>("From", _settings.FromNumber),
            new KeyValuePair<string, string>("DateSent>", fromDate),
            new KeyValuePair<string, string>("DateSent<", throughDate),
            new KeyValuePair<string, string>("PageSize", "1000")
        });

        var next = $"{MessagesPath()}?{query}";
        var messages = new List<ProviderMessage>();
        while (!string.IsNullOrWhiteSpace(next))
        {
            using var request = CreateRequest(HttpMethod.Get, BuildMessagingUri(next));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            using var json = await ReadResponseAsync(response, cancellationToken);
            var root = json.RootElement;
            if (root.TryGetProperty("messages", out var items))
            {
                messages.AddRange(items.EnumerateArray().Select(ParseMessage));
            }

            next = root.TryGetProperty("next_page_uri", out var nextElement) &&
                   nextElement.ValueKind != JsonValueKind.Null
                ? nextElement.GetString()
                : null;
        }

        return messages.Where(x =>
        {
            var timestamp = x.DateSent ?? x.DateCreated;
            return timestamp.HasValue && timestamp.Value >= from && timestamp.Value <= to;
        }).ToList();
    }

    private async Task<ProviderMessage> SendMessageRequestAsync(HttpMethod method, string path,
        IEnumerable<KeyValuePair<string, string>>? form, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, BuildMessagingUri(path));
        if (form != null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        using var json = await ReadResponseAsync(response, cancellationToken);
        return ParseMessage(json.RootElement);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task<JsonDocument> ReadResponseAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return json;
        }

        int? code = null;
        if (json.RootElement.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsed))
        {
            code = parsed;
        }

        json.Dispose();
        throw new TwilioProviderException((int)response.StatusCode, code);
    }

    private static ProviderMessage ParseMessage(JsonElement element)
    {
        return new ProviderMessage(
            GetString(element, "sid") ?? string.Empty,
            GetString(element, "status") ?? "unknown",
            GetNullableInt(element, "error_code"),
            ParseDate(GetString(element, "date_created")),
            ParseDate(GetString(element, "date_sent")),
            GetString(element, "body"));
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static int? GetNullableInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var result) ? result : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces,
            out var result) ? result : null;

    private string MessagesPath() => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";

    private string MessagePath(string sid) =>
        $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private string BuildMessagingUri(string pathOrUri)
    {
        var path = Uri.TryCreate(pathOrUri, UriKind.Absolute, out var absolute)
            ? absolute.PathAndQuery
            : pathOrUri;
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl;
        return $"{baseUrl!.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private static string EncodeQuery(IEnumerable<KeyValuePair<string, string>> values) =>
        string.Join("&", values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio credentials are not configured.");
        }
    }

    private void EnsureMessagingConfiguration(bool scheduled)
    {
        EnsureCredentials();
        if (string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            throw new InvalidOperationException("Twilio sending number is not configured.");
        }

        if (scheduled && string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio messaging service is required for scheduled messages.");
        }
    }
}
