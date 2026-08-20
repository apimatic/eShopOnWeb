using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string ScheduleTypeFixed = "fixed";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
    }

    public Task<ProviderMessage> SendMessageAsync(SendProviderMessageRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body),
            new("From", _settings.FromNumber)
        };

        if (request.SendAt.HasValue)
        {
            fields.Add(new KeyValuePair<string, string>("MessagingServiceSid", _settings.MessagingServiceSid));
            fields.Add(new KeyValuePair<string, string>("ScheduleType", ScheduleTypeFixed));
            fields.Add(new KeyValuePair<string, string>("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")));
        }

        var path = $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
        return SendFormAsync(HttpMethod.Post, path, fields, cancellationToken, expectedCreated: true);
    }

    public Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var path = $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";
        return SendFormAsync(HttpMethod.Get, path, null, cancellationToken, expectedCreated: false);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var query = string.Join("&",
            $"From={Uri.EscapeDataString(fromNumber)}",
            $"{Uri.EscapeDataString("DateSent>")}={Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"))}",
            $"{Uri.EscapeDataString("DateSent<")}={Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"))}",
            "PageSize=1000");

        var path = $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json?{query}";
        var results = new List<ProviderMessage>();
        var nextPath = path;

        while (!string.IsNullOrWhiteSpace(nextPath))
        {
            using var request = CreateRequest(HttpMethod.Get, MessagingUri(nextPath));
            using var response = await SendWithoutLeakingDestinationAsync(request, cancellationToken);
            await EnsureSuccessWithoutLeakingDestinationAsync(response, cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<TwilioListMessageResponse>(JsonOptions, cancellationToken);
            if (page?.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToProviderMessage(message));
                }
            }

            nextPath = NormalizeNextPagePath(page?.NextPageUri);
        }

        return results;
    }

    public Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Status", "canceled")
        };
        var path = $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";
        return SendFormAsync(HttpMethod.Post, path, fields, cancellationToken, expectedCreated: false);
    }

    public Task<ProviderMessage> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var path = $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";
        return SendFormAsync(
            HttpMethod.Post,
            path,
            fields: null,
            cancellationToken,
            expectedCreated: false,
            rawFormBody: "Body=",
            allowQueuedConflict: true);
    }

    private async Task<ProviderMessage> SendFormAsync(
        HttpMethod method,
        string relativePath,
        List<KeyValuePair<string, string>>? fields,
        CancellationToken cancellationToken,
        bool expectedCreated,
        string? rawFormBody = null,
        bool allowQueuedConflict = false)
    {
        using var request = CreateRequest(method, MessagingUri(relativePath));
        if (rawFormBody is not null)
        {
            var content = new ByteArrayContent(Encoding.ASCII.GetBytes(rawFormBody));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");
            request.Content = content;
        }
        else if (fields is not null)
        {
            request.Content = new FormUrlEncodedContent(fields);
        }

        using var response = await SendWithoutLeakingDestinationAsync(request, cancellationToken);
        if (allowQueuedConflict && await IsQueuedConflictAsync(response, cancellationToken))
        {
            var sid = relativePath.Split('/').LastOrDefault()?.Replace(".json", string.Empty);
            if (!string.IsNullOrWhiteSpace(sid))
            {
                return await FetchMessageAsync(sid, cancellationToken);
            }
        }

        await EnsureSuccessWithoutLeakingDestinationAsync(response, cancellationToken);

        var resource = await response.Content.ReadFromJsonAsync<TwilioMessageResource>(JsonOptions, cancellationToken);
        if (resource is null || string.IsNullOrWhiteSpace(resource.Sid))
        {
            throw new InvalidOperationException("The messaging provider returned an unreadable message resource.");
        }

        _ = expectedCreated;
        return ToProviderMessage(resource);
    }

    private static async Task<bool> IsQueuedConflictAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if ((int)response.StatusCode != 409)
        {
            return false;
        }

        try
        {
            var error = await response.Content.ReadFromJsonAsync<TwilioRestError>(JsonOptions, cancellationToken);
            return error?.Code == 20409;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = TwilioAuth.CreateBasicHeader(_settings);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private Uri MessagingUri(string relativePathAndQuery)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.TrimEnd('/');
        var relative = relativePathAndQuery.TrimStart('/');
        return new Uri($"{baseUrl}/{relative}");
    }

    private static string? NormalizeNextPagePath(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery.TrimStart('/');
        }

        return nextPageUri.TrimStart('/');
    }

    private async Task<HttpResponseMessage> SendWithoutLeakingDestinationAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception)
        {
            throw new InvalidOperationException("The messaging provider request failed.");
        }
    }

    private static async Task EnsureSuccessWithoutLeakingDestinationAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? providerCode = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TwilioRestError>(JsonOptions, cancellationToken);
            providerCode = error?.Code;
        }
        catch (Exception)
        {
            // The error body is not used beyond the numeric code so shopper numbers cannot leak.
        }

        throw new InvalidOperationException(
            providerCode.HasValue
                ? $"The messaging provider request failed with HTTP {(int)response.StatusCode} and code {providerCode.Value}."
                : $"The messaging provider request failed with HTTP {(int)response.StatusCode}.");
    }

    private static ProviderMessage ToProviderMessage(TwilioMessageResource resource)
    {
        return new ProviderMessage(
            resource.Sid ?? string.Empty,
            resource.Status,
            resource.From,
            resource.To,
            resource.Body,
            resource.ErrorCode,
            resource.ErrorMessage,
            resource.DateSent,
            resource.DateCreated);
    }
}
