using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Twilio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public abstract class TwilioApiClientBase
{
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IOptions<TwilioOptions> _options;

    protected TwilioApiClientBase(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        HttpClient = httpClient;
        _options = options;
        ApplyAuthentication(httpClient, options.Value);
    }

    protected HttpClient HttpClient { get; }
    protected TwilioOptions Options => _options.Value;

    protected static void ApplyAuthentication(HttpClient httpClient, TwilioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccountSid) || string.IsNullOrWhiteSpace(options.AuthToken))
        {
            return;
        }

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    protected static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        int? code = null;
        var message = $"Twilio request failed with status {(int)response.StatusCode}.";
        try
        {
            var error = JsonSerializer.Deserialize<TwilioErrorResponse>(body, JsonOptions);
            if (error is not null)
            {
                code = error.Code;
                if (!string.IsNullOrWhiteSpace(error.Message))
                {
                    message = error.Message;
                }
            }
        }
        catch (JsonException)
        {
            // Spec does not guarantee a body on every error; keep the generic status message.
        }

        throw new TwilioApiException(response.StatusCode, code, message);
    }

    protected static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }
}
