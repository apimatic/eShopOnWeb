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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio implementation of <see cref="ISmsGateway"/>. Talks to the classic messaging API
/// (api.twilio.com/2010-04-01, or the configured BaseUrl override) for sending, reading,
/// scheduling, cancelling, redacting and listing messages, and to the Lookup API
/// (lookups.twilio.com) for number validation. Never logs phone numbers or credentials.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsGateway> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioSmsGateway(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioSmsGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio settings are incomplete: Twilio:AccountSid and Twilio:AuthToken are required.");
        }
    }

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // The free formatting/validation call: no Fields parameter. The leading '+' of an
        // E.164 input must be percent-encoded in the path, which EscapeDataString does.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        var json = doc.RootElement;

        if (!response.IsSuccessStatusCode)
        {
            // Lookup v2 answers 200 with valid=false for bad numbers; anything else is treated as not usable.
            return new PhoneNumberValidationResult
            {
                IsValid = false,
                ValidationErrors = new[] { GetErrorMessage(json) ?? $"Lookup failed with status {(int)response.StatusCode}." }
            };
        }

        var isValid = json.TryGetProperty("valid", out var validProp) && validProp.GetBoolean();
        var errors = json.TryGetProperty("validation_errors", out var errorsProp) && errorsProp.ValueKind == JsonValueKind.Array
            ? errorsProp.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToArray()
            : Array.Empty<string>();

        return new PhoneNumberValidationResult
        {
            IsValid = isValid,
            CanonicalNumber = isValid ? GetString(json, "phone_number") : null,
            NationalFormat = isValid ? GetString(json, "national_format") : null,
            ValidationErrors = errors
        };
    }

    public async Task<SendMessageResult> SendMessageAsync(string to, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _settings.FromNumber),
            new("Body", body)
        };

        if (sendAt.HasValue)
        {
            // Scheduling requires a Messaging Service; the provider holds the message until SendAt.
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        using var response = await SendAsync(HttpMethod.Post, MessagesUrl(), form, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        var json = doc.RootElement;

        if (!response.IsSuccessStatusCode)
        {
            return new SendMessageResult
            {
                Accepted = false,
                ErrorCode = GetInt(json, "code"),
                ErrorMessage = GetErrorMessage(json) ?? $"Message create failed with status {(int)response.StatusCode}."
            };
        }

        return new SendMessageResult
        {
            Accepted = true,
            MessageSid = GetString(json, "sid"),
            Status = GetString(json, "status")
        };
    }

    public async Task<ProviderMessageState> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, MessageUrl(messageSid), null, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        var json = doc.RootElement;
        response.EnsureSuccessStatusCode();
        return ParseMessage(json);
    }

    public async Task<ProviderMessageState> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // A message resource can 404 for a short window right after creation; retry the
        // cancel rather than leaving a scheduled message to go out.
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
            using var response = await SendAsync(HttpMethod.Post, MessageUrl(messageSid), form, cancellationToken);
            using var doc = await ReadJsonAsync(response, cancellationToken);
            var json = doc.RootElement;

            if (response.IsSuccessStatusCode)
            {
                return ParseMessage(json);
            }

            var retriable = response.StatusCode == System.Net.HttpStatusCode.NotFound || (int)response.StatusCode >= 500;
            if (!retriable || attempt == maxAttempts)
            {
                response.EnsureSuccessStatusCode();
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
        }
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // An empty Body value redacts the message text at the provider. Same create-then-read
        // race as cancel: a fresh message resource can 404 briefly, so retry.
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
            using var response = await SendAsync(HttpMethod.Post, MessageUrl(messageSid), form, cancellationToken);
            using var doc = await ReadJsonAsync(response, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var retriable = response.StatusCode == System.Net.HttpStatusCode.NotFound || (int)response.StatusCode >= 500;
            if (!retriable || attempt == maxAttempts)
            {
                response.EnsureSuccessStatusCode();
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProviderMessageState>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's own sending number's messages only.
        // Date filters are date-granular, so widen by a day on each side and refine in memory.
        var query = new List<KeyValuePair<string, string>>
        {
            new("From", _settings.FromNumber),
            new("DateSent>", from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("DateSent<", to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("PageSize", "1000")
        };

        var results = new List<ProviderMessageState>();
        string? nextUri = $"{MessagesUrl()}?{BuildQuery(query)}";

        while (nextUri is not null)
        {
            var url = nextUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? nextUri
                : $"{_messagingBaseUrl}{nextUri}";

            using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
            using var doc = await ReadJsonAsync(response, cancellationToken);
        var json = doc.RootElement;
            response.EnsureSuccessStatusCode();

            if (json.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                results.AddRange(messages.EnumerateArray().Select(ParseMessage));
            }

            nextUri = json.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }

        return results
            .Where(m =>
            {
                var when = m.DateSent ?? m.DateCreated;
                return when.HasValue && when.Value >= from && when.Value <= to;
            })
            .ToList();
    }

    private string MessagesUrl() => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, List<KeyValuePair<string, string>>? form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if ((int)response.StatusCode >= 500)
        {
            _logger.LogWarning("Twilio request {Method} {Path} failed with status {StatusCode}.", method, request.RequestUri?.AbsolutePath, (int)response.StatusCode);
        }
        return response;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return JsonDocument.Parse("{}");
        }
        return JsonDocument.Parse(content);
    }

    private static ProviderMessageState ParseMessage(JsonElement json)
    {
        return new ProviderMessageState
        {
            MessageSid = GetString(json, "sid") ?? string.Empty,
            Status = GetString(json, "status") ?? string.Empty,
            ErrorCode = GetInt(json, "error_code"),
            ErrorMessage = GetString(json, "error_message"),
            To = GetString(json, "to"),
            From = GetString(json, "from"),
            DateCreated = GetRfc2822Date(json, "date_created"),
            DateSent = GetRfc2822Date(json, "date_sent")
        };
    }

    private static string? GetString(JsonElement json, string name)
        => json.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

    private static int? GetInt(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var prop))
        {
            return null;
        }
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value))
        {
            return value;
        }
        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out value))
        {
            return value;
        }
        return null;
    }

    private static string? GetErrorMessage(JsonElement json) => GetString(json, "message");

    private static DateTimeOffset? GetRfc2822Date(JsonElement json, string name)
    {
        var value = GetString(json, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        // The classic API returns RFC 2822 timestamps, e.g. "Thu, 24 Aug 2023 05:01:45 +0000".
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> parameters)
        => string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
}
