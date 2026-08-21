using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written client for the Twilio Messaging API (twilio_api_v2010 Message resource).
/// Twilio:BaseUrl, when set, is the base address for every call this client makes.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        HttpClient httpClient,
        IOptions<TwilioOptions> options,
        ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProviderMessage> CreateMessageAsync(
        string to,
        string body,
        DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body)
        };

        if (sendAt.HasValue)
        {
            // CreateMessage scheduled SMS: ScheduleType=fixed, SendAt (ISO-8601), MessagingServiceSid required.
            fields.Add(new KeyValuePair<string, string>("MessagingServiceSid", _options.MessagingServiceSid));
            fields.Add(new KeyValuePair<string, string>("ScheduleType", "fixed"));
            fields.Add(new KeyValuePair<string, string>("SendAt", sendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")));
        }
        else
        {
            fields.Add(new KeyValuePair<string, string>("From", _options.FromNumber));
            if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            {
                fields.Add(new KeyValuePair<string, string>("MessagingServiceSid", _options.MessagingServiceSid));
            }
        }

        using var content = new FormUrlEncodedContent(fields);
        var path = $"2010-04-01/Accounts/{_options.AccountSid}/Messages.json";
        using var response = await _httpClient.PostAsync(path, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio CreateMessage failed with HTTP {StatusCode}.", (int)response.StatusCode);
            await TwilioHttp.ThrowForErrorAsync(response, "CreateMessage", cancellationToken);
        }

        var resource = await TwilioHttp.ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
        return ToProviderMessage(resource);
    }

    public async Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var path = $"2010-04-01/Accounts/{_options.AccountSid}/Messages/{messageSid}.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio FetchMessage failed with HTTP {StatusCode}.", (int)response.StatusCode);
            await TwilioHttp.ThrowForErrorAsync(response, "FetchMessage", cancellationToken);
        }

        var resource = await TwilioHttp.ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
        return ToProviderMessage(resource);
    }

    public async Task<ProviderMessage> UpdateMessageAsync(
        string messageSid,
        string? body,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var path = $"2010-04-01/Accounts/{_options.AccountSid}/Messages/{messageSid}.json";
        HttpResponseMessage? response = null;

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var fields = new List<KeyValuePair<string, string>>();
            if (body != null)
            {
                fields.Add(new KeyValuePair<string, string>("Body", body));
            }

            if (!string.IsNullOrEmpty(status))
            {
                fields.Add(new KeyValuePair<string, string>("Status", status));
            }

            using var content = new StringContent(
                string.Join("&", fields.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}")),
                Encoding.ASCII,
                "application/x-www-form-urlencoded");

            response?.Dispose();
            response = await _httpClient.PostAsync(path, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                break;
            }

            if ((int)response.StatusCode == 404 && attempt < 5)
            {
                _logger.LogInformation("Twilio UpdateMessage returned HTTP 404; retrying attempt {Attempt}.", attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), cancellationToken);
                continue;
            }

            _logger.LogWarning("Twilio UpdateMessage failed with HTTP {StatusCode}.", (int)response.StatusCode);
            await TwilioHttp.ThrowForErrorAsync(response, "UpdateMessage", cancellationToken);
        }

        using (response)
        {
            var resource = await TwilioHttp.ReadJsonAsync<TwilioMessageResource>(response!, cancellationToken);
            return ToProviderMessage(resource);
        }
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessage>();
        var path = BuildListPath(fromNumber, from, to);

        while (!string.IsNullOrEmpty(path))
        {
            using var response = await _httpClient.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Twilio ListMessage failed with HTTP {StatusCode}.", (int)response.StatusCode);
                await TwilioHttp.ThrowForErrorAsync(response, "ListMessage", cancellationToken);
            }

            var page = await TwilioHttp.ReadJsonAsync<TwilioListMessageResponse>(response, cancellationToken);
            if (page.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToProviderMessage(message));
                }
            }

            path = ToRelativePath(page.NextPageUri);
        }

        return results;
    }

    private string BuildListPath(string fromNumber, DateTimeOffset from, DateTimeOffset to)
    {
        var fromEncoded = Uri.EscapeDataString(fromNumber);
        var fromDate = Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        var toDate = Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        return $"2010-04-01/Accounts/{_options.AccountSid}/Messages.json?From={fromEncoded}&DateSent%3E={fromDate}&DateSent%3C={toDate}&PageSize=1000";
    }

    private static string? ToRelativePath(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri))
        {
            return null;
        }

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery.TrimStart('/');
        }

        return nextPageUri.TrimStart('/');
    }

    private static ProviderMessage ToProviderMessage(TwilioMessageResource resource)
    {
        return new ProviderMessage
        {
            Sid = resource.Sid,
            Status = resource.Status,
            ErrorCode = resource.ErrorCode,
            ErrorMessage = resource.ErrorMessage,
            Body = resource.Body,
            From = resource.From,
            To = resource.To,
            DateSent = resource.DateSent,
            DateCreated = resource.DateCreated,
            Direction = resource.Direction,
            MessagingServiceSid = resource.MessagingServiceSid
        };
    }
}
