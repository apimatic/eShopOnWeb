using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public sealed class FinixClient : IFinixClient
{
    private const string FinixVersion = "2022-02-01";
    private const string SandboxBaseUrl = "https://finix.sandbox-payments-api.com";
    private const string LiveBaseUrl = "https://finix.live-payments-api.com";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly FinixOptions _options;

    public FinixClient(HttpClient httpClient, IOptions<FinixOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password))
        {
            throw new InvalidOperationException("Finix credentials are not configured. Set Finix:Username and Finix:Password.");
        }

        if (string.IsNullOrWhiteSpace(_options.MerchantId))
        {
            throw new InvalidOperationException("Finix merchant is not configured. Set Finix:MerchantId.");
        }

        if (string.IsNullOrWhiteSpace(_options.Currency))
        {
            throw new InvalidOperationException("Finix currency is not configured. Set Finix:Currency.");
        }
    }

    public string Currency => _options.Currency!;
    public string MerchantId => _options.MerchantId!;

    public Task<FinixIdentity> CreateBuyerIdentityAsync(string email, string? firstName, string? lastName,
        FinixAddress? address, CancellationToken cancellationToken = default)
    {
        var request = new FinixCreateIdentityRequest
        {
            Entity = new FinixIdentityEntity
            {
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                PersonalAddress = address
            },
            IdentityRoles = new List<string> { "BUYER" },
            Type = "PERSONAL"
        };

        return SendAsync<FinixIdentity>(HttpMethod.Post, "identities", request, cancellationToken);
    }

    public Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(FinixCardDetails card, string identityId,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixCreatePaymentInstrumentRequest
        {
            Address = card.Address,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            Identity = identityId,
            Name = card.Name,
            Number = card.Number,
            SecurityCode = card.SecurityCode,
            Type = "PAYMENT_CARD"
        };

        return SendAsync<FinixPaymentInstrument>(HttpMethod.Post, "payment_instruments", request, cancellationToken);
    }

    public async Task DisablePaymentInstrumentAsync(string paymentInstrumentId,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixUpdatePaymentInstrumentRequest { Enabled = false };
        await SendAsync<FinixPaymentInstrument>(HttpMethod.Put,
            $"payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}", request, cancellationToken);
    }

    public Task<FinixAuthorization> CreateAuthorizationAsync(int amountCents, string source,
        string idempotencyId, IReadOnlyDictionary<string, string>? tags, string? securityCode = null,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixCreateAuthorizationRequest
        {
            Amount = amountCents,
            Currency = Currency,
            Merchant = MerchantId,
            Source = source,
            IdempotencyId = idempotencyId,
            SecurityCode = securityCode,
            Tags = tags is null ? null : new Dictionary<string, string>(tags)
        };

        return SendAsync<FinixAuthorization>(HttpMethod.Post, "authorizations", request, cancellationToken);
    }

    public Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixAuthorization>(HttpMethod.Get,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}", null, cancellationToken);
    }

    public Task<FinixAuthorization> CaptureAuthorizationAsync(string authorizationId, int captureAmountCents,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixCaptureAuthorizationRequest { CaptureAmount = captureAmountCents };
        return SendAsync<FinixAuthorization>(HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}", request, cancellationToken);
    }

    public Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixVoidAuthorizationRequest { VoidMe = true };
        return SendAsync<FinixAuthorization>(HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}", request, cancellationToken);
    }

    public Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixTransfer>(HttpMethod.Get,
            $"transfers/{Uri.EscapeDataString(transferId)}", null, cancellationToken);
    }

    public Task<FinixTransfer> CreateReversalAsync(string transferId, int refundAmountCents,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixCreateReversalRequest { RefundAmount = refundAmountCents };
        return SendAsync<FinixTransfer>(HttpMethod.Post,
            $"transfers/{Uri.EscapeDataString(transferId)}/reversals", request, cancellationToken);
    }

    public async Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FinixTransfer>();
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);

        while (true)
        {
            var path = BuildListPath("transfers", from, to, cursor);
            var page = await SendAsync<FinixTransferListResponse>(HttpMethod.Get, path, null, cancellationToken);

            if (page.Embedded?.Transfers is { Count: > 0 })
            {
                results.AddRange(page.Embedded.Transfers);
            }

            cursor = page.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor) || !seenCursors.Add(cursor))
            {
                break;
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<FinixAuthorization>();
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);

        while (true)
        {
            var path = BuildListPath("authorizations", from, to, cursor);
            var page = await SendAsync<FinixAuthorizationListResponse>(HttpMethod.Get, path, null,
                cancellationToken);

            if (page.Embedded?.Authorizations is { Count: > 0 })
            {
                results.AddRange(page.Embedded.Authorizations);
            }

            cursor = page.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor) || !seenCursors.Add(cursor))
            {
                break;
            }
        }

        return results;
    }

    private static string BuildListPath(string resource, DateTimeOffset from, DateTimeOffset to, string? cursor)
    {
        var query = new StringBuilder();
        query.Append("?limit=100");
        query.Append("&created_at.gte=").Append(Uri.EscapeDataString(FormatDate(from)));
        query.Append("&created_at.lte=").Append(Uri.EscapeDataString(FormatDate(to)));
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            query.Append("&after_cursor=").Append(Uri.EscapeDataString(cursor));
        }

        return resource + query;
    }

    private static string FormatDate(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}")));
        request.Headers.Add("Finix-Version", FinixVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, responseBody);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            throw new FinixApiException((int)response.StatusCode, null,
                "Finix returned an empty response body.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(responseBody, SerializerOptions)
                   ?? throw new FinixApiException((int)response.StatusCode, null,
                       "Finix returned an empty JSON document.");
        }
        catch (JsonException ex)
        {
            throw new FinixApiException((int)response.StatusCode, null,
                $"Finix returned a response that could not be read: {ex.Message}");
        }
    }

    private Uri BuildUri(string path)
    {
        var baseUrl = !string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? _options.BaseUrl!
            : string.Equals(_options.Environment, "live", StringComparison.OrdinalIgnoreCase)
                ? LiveBaseUrl
                : SandboxBaseUrl;

        var normalizedBaseUrl = baseUrl.TrimEnd('/') + "/";
        return new Uri(new Uri(normalizedBaseUrl, UriKind.Absolute), path.TrimStart('/'));
    }

    private static FinixApiException BuildException(HttpStatusCode statusCode, string responseBody)
    {
        string? errorCode = null;
        var messages = new List<string>();

        try
        {
            var envelope = JsonSerializer.Deserialize<FinixErrorEnvelope>(responseBody, SerializerOptions);
            if (envelope?.Embedded?.Errors is { Count: > 0 } errors)
            {
                errorCode = errors[0].Code;
                foreach (var error in errors)
                {
                    if (!string.IsNullOrWhiteSpace(error.Message))
                    {
                        messages.Add(error.Message!);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Fall back to a generic message below.
        }

        var message = messages.Count > 0
            ? string.Join("; ", messages)
            : $"Finix returned HTTP {(int)statusCode} ({statusCode}).";

        return new FinixApiException((int)statusCode, errorCode, message, responseBody);
    }
}

