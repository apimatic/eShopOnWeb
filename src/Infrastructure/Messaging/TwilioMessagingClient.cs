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

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessagingClient : IOrderSmsGateway
{
    public const string HttpClientName = "TwilioMessaging";
    public const string DefaultBaseUrl = "https://api.twilio.com";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<TwilioOptions> _options;
    private readonly IAppLogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioOptions> options,
        IAppLogger<TwilioMessagingClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public string ConfiguredFromNumber => _options.Value.FromNumber;

    public Task<ProviderSmsMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = options.FromNumber,
            ["Body"] = body
        };

        if (!string.IsNullOrWhiteSpace(options.MessagingServiceSid))
        {
            fields["MessagingServiceSid"] = options.MessagingServiceSid;
        }

        return CreateMessageAsync(fields, cancellationToken);
    }

    public Task<ProviderSmsMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body,
            ["MessagingServiceSid"] = options.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(options.FromNumber))
        {
            fields["From"] = options.FromNumber;
        }

        return CreateMessageAsync(fields, cancellationToken);
    }

    public async Task<ProviderSmsMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await SendMessagingAsync(HttpMethod.Get, MessagePath(messageSid), content: null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await ReadPayloadAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Fetch message failed with HTTP {Status} provider code {Code}", (int)response.StatusCode, ReadProviderCode(payload) ?? "none");
            throw new InvalidOperationException($"Fetch message failed with HTTP {(int)response.StatusCode}.");
        }

        return ParseMessage(payload);
    }

    public async Task<ProviderSmsMessage?> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "canceled" });
        using var response = await SendMessagingAsync(HttpMethod.Post, MessagePath(messageSid), content, cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Cancel scheduled message failed with HTTP {Status} provider code {Code} detail {Detail}", (int)response.StatusCode, ReadProviderCode(payload) ?? "none", ReadProviderDetail(payload) ?? "none");
            throw new InvalidOperationException($"Cancel scheduled message failed with HTTP {(int)response.StatusCode}.");
        }

        return ParseMessage(payload);
    }

    public async Task<ProviderSmsMessage?> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Body"] = string.Empty });
        using var response = await SendMessagingAsync(HttpMethod.Post, MessagePath(messageSid), content, cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Redact message failed with HTTP {Status} provider code {Code}", (int)response.StatusCode, ReadProviderCode(payload) ?? "none");
            throw new InvalidOperationException($"Redact message failed with HTTP {(int)response.StatusCode}.");
        }

        return ParseMessage(payload);
    }

    public async Task<IReadOnlyList<ProviderSmsMessage>> ListFromConfiguredSenderAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromNumber = _options.Value.FromNumber;
        var fromIso = from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var toIso = to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var pathAndQuery =
            $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.Value.AccountSid)}/Messages.json" +
            $"?From={Uri.EscapeDataString(fromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromIso)}" +
            $"&DateSent%3C={Uri.EscapeDataString(toIso)}" +
            "&PageSize=1000";

        var results = new List<ProviderSmsMessage>();
        string? next = pathAndQuery;

        while (!string.IsNullOrEmpty(next))
        {
            using var response = await SendMessagingAsync(HttpMethod.Get, next, content: null, cancellationToken);
            var payload = await ReadPayloadAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("List messages failed with HTTP {Status} provider code {Code}", (int)response.StatusCode, ReadProviderCode(payload) ?? "none");
                throw new InvalidOperationException($"List messages failed with HTTP {(int)response.StatusCode}.");
            }

            using var document = JsonDocument.Parse(string.IsNullOrEmpty(payload) ? "{}" : payload);
            if (document.RootElement.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in messages.EnumerateArray())
                {
                    var parsed = ParseMessageElement(item);
                    if (parsed != null)
                    {
                        results.Add(parsed);
                    }
                }
            }

            next = null;
            if (document.RootElement.TryGetProperty("next_page_uri", out var nextUri) && nextUri.ValueKind == JsonValueKind.String)
            {
                var raw = nextUri.GetString();
                if (!string.IsNullOrEmpty(raw))
                {
                    next = ToMessagingRelativePath(raw);
                }
            }
        }

        return results;
    }

    private async Task<ProviderSmsMessage> CreateMessageAsync(Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        var path = $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.Value.AccountSid)}/Messages.json";
        using var content = new FormUrlEncodedContent(fields);
        using var response = await SendMessagingAsync(HttpMethod.Post, path, content, cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Create message failed with HTTP {Status} provider code {Code}", (int)response.StatusCode, ReadProviderCode(payload) ?? "none");
            throw new InvalidOperationException($"Create message failed with HTTP {(int)response.StatusCode}.");
        }

        var parsed = ParseMessage(payload);
        if (parsed == null)
        {
            throw new InvalidOperationException("Create message returned an empty provider payload.");
        }

        return parsed;
    }

    private async Task<HttpResponseMessage> SendMessagingAsync(HttpMethod method, string relativePath, HttpContent? content, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var url = ResolveBaseUrl().TrimEnd('/') + "/" + TrimLeadingSlash(relativePath);
        using var request = new HttpRequestMessage(method, url)
        {
            Version = new Version(1, 1)
        };
        request.Headers.Authorization = CreateBasicAuth(_options.Value);
        if (content != null)
        {
            await content.LoadIntoBufferAsync();
            request.Content = content;
        }

        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var path = response.RequestMessage?.RequestUri?.PathAndQuery;
            _logger.LogWarning("Messaging {Method} failed for path {Path} with HTTP {Status}", method.Method, path ?? relativePath, (int)response.StatusCode);
        }

        return response;
    }

    private string ResolveBaseUrl()
    {
        var configured = _options.Value.BaseUrl;
        var baseUrl = string.IsNullOrWhiteSpace(configured) ? DefaultBaseUrl : configured.Trim();
        return baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/";
    }

    private string MessagePath(string messageSid) =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.Value.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private string ToMessagingRelativePath(string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return TrimLeadingSlash(absolute.PathAndQuery);
        }

        return TrimLeadingSlash(nextPageUri);
    }

    private static string TrimLeadingSlash(string value) =>
        value.StartsWith("/", StringComparison.Ordinal) ? value.Substring(1) : value;

    private static AuthenticationHeaderValue CreateBasicAuth(TwilioOptions options)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private static async Task<string> ReadPayloadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadAsStringAsync(cancellationToken);

    private static string? ReadProviderDetail(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrEmpty(payload) ? "{}" : payload);
            if (document.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                var text = message.GetString();
                if (string.IsNullOrEmpty(text) || text.IndexOf('+') >= 0)
                {
                    return null;
                }

                return text;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string? ReadProviderCode(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrEmpty(payload) ? "{}" : payload);
            if (document.RootElement.TryGetProperty("code", out var code))
            {
                return code.ToString();
            }
        }
        catch (JsonException)
        {
            // Body is not JSON; never return it (it may contain destination numbers).
        }

        return null;
    }

    private static ProviderSmsMessage? ParseMessage(string payload)
    {
        using var document = JsonDocument.Parse(string.IsNullOrEmpty(payload) ? "{}" : payload);
        return ParseMessageElement(document.RootElement);
    }

    private static ProviderSmsMessage? ParseMessageElement(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var sid = ReadString(root, "sid");
        if (string.IsNullOrEmpty(sid))
        {
            return null;
        }

        return new ProviderSmsMessage(
            sid,
            ReadString(root, "status") ?? "unknown",
            ReadString(root, "body"),
            ReadString(root, "to"),
            ReadString(root, "from"),
            ReadErrorCode(root),
            ParseTwilioDate(ReadString(root, "date_sent")),
            ParseTwilioDate(ReadString(root, "date_created")));
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? ReadErrorCode(JsonElement root)
    {
        if (!root.TryGetProperty("error_code", out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ToString();
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
