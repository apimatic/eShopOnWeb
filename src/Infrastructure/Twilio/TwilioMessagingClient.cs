using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioMessagingClient : ISmsGateway
{
    public const string HttpClientName = "TwilioMessaging";
    public const string DefaultBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        ApplyAuth(_httpClient, _options);
        _httpClient.BaseAddress ??= new Uri(ResolveMessagingBase());
    }

    public string FromNumber => _options.FromNumber;

    public async Task<SmsMessageSnapshot> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body
        };

        if (!string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            fields["From"] = _options.FromNumber;
        }

        if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            fields["MessagingServiceSid"] = _options.MessagingServiceSid;
        }

        if (request.SendAt is not null)
        {
            fields["ScheduleType"] = "fixed";
            fields["SendAt"] = request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        using var content = new FormUrlEncodedContent(fields);
        using var response = await _httpClient.PostAsync(ResolveMessagingUri(MessagesCollectionPath()), content, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.Created && !response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            throw new TwilioApiException((int)response.StatusCode, error?.Code);
        }

        var resource = await response.Content.ReadFromJsonAsync<TwilioMessageResource>(JsonOptions, cancellationToken)
            ?? throw new TwilioApiException((int)response.StatusCode, null);
        return ToSnapshot(resource);
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(ResolveMessagingUri(MessageInstancePath(providerMessageSid)), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            throw new TwilioApiException((int)response.StatusCode, error?.Code);
        }

        var resource = await response.Content.ReadFromJsonAsync<TwilioMessageResource>(JsonOptions, cancellationToken);
        return resource is null ? null : ToSnapshot(resource);
    }

    public async Task<SmsMessageSnapshot> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Status"] = "canceled"
        });
        using var response = await _httpClient.PostAsync(ResolveMessagingUri(MessageInstancePath(providerMessageSid)), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            throw new TwilioApiException((int)response.StatusCode, error?.Code);
        }

        var resource = await response.Content.ReadFromJsonAsync<TwilioMessageResource>(JsonOptions, cancellationToken)
            ?? throw new TwilioApiException((int)response.StatusCode, null);
        return ToSnapshot(resource);
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Spec: Body must be an empty string to redact. Send the field explicitly so it is not dropped.
        using var content = new StringContent("Body=", Encoding.UTF8, "application/x-www-form-urlencoded");
        using var response = await _httpClient.PostAsync(ResolveMessagingUri(MessageInstancePath(providerMessageSid)), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            throw new TwilioApiException((int)response.StatusCode, error?.Code);
        }
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }

        var results = new List<SmsMessageSnapshot>();
        var fromUtc = from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var toUtc = to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        // Ask the provider for this application's sending number rather than listing the whole account.
        var next = $"{MessagesCollectionPath()}?From={Uri.EscapeDataString(_options.FromNumber)}&{Uri.EscapeDataString("DateSent>")}={Uri.EscapeDataString(fromUtc)}&{Uri.EscapeDataString("DateSent<")}={Uri.EscapeDataString(toUtc)}&PageSize=1000";

        while (!string.IsNullOrEmpty(next))
        {
            using var response = await _httpClient.GetAsync(ResolveMessagingUri(next), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorAsync(response, cancellationToken);
                throw new TwilioApiException((int)response.StatusCode, error?.Code);
            }

            var page = await response.Content.ReadFromJsonAsync<TwilioMessageListResponse>(JsonOptions, cancellationToken);
            if (page?.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            next = page?.NextPageUri;
        }

        return results;
    }

    private string MessagesCollectionPath()
        => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessageInstancePath(string sid)
        => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private string ResolveMessagingBase()
    {
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return _options.BaseUrl.TrimEnd('/');
        }

        return DefaultBaseUrl;
    }

    private Uri ResolveMessagingUri(string uriOrPath)
    {
        var baseAddress = ResolveMessagingBase();
        if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var absolute))
        {
            if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
            {
                return new Uri(baseAddress + absolute.PathAndQuery);
            }

            return absolute;
        }

        var path = uriOrPath.StartsWith('/') ? uriOrPath : "/" + uriOrPath;
        return new Uri(baseAddress + path);
    }

    internal static void ApplyAuth(HttpClient client, TwilioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccountSid) || string.IsNullOrWhiteSpace(options.AuthToken))
        {
            return;
        }

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static SmsMessageSnapshot ToSnapshot(TwilioMessageResource resource)
        => new(
            resource.Sid,
            resource.Status,
            resource.Body,
            resource.From,
            resource.To,
            resource.DateCreated,
            resource.DateSent,
            resource.ErrorCode,
            resource.ErrorMessage);

    private static async Task<TwilioApiError?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<TwilioApiError>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
