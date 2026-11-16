using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Finix;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public class FinixClient : IFinixClient
{
    private const string FinixVersion = "2022-02-01";
    private const int PageLimit = 100;

    private readonly HttpClient _httpClient;
    private readonly IOptions<FinixOptions> _options;
    private readonly ILogger<FinixClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public FinixClient(HttpClient httpClient, IOptions<FinixOptions> options, ILogger<FinixClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<FinixIdentityResponse> CreateBuyerIdentityAsync(string? email, string? name,
        CancellationToken cancellationToken = default)
    {
        var entity = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(email))
        {
            entity["email"] = email;
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            entity["first_name"] = name;
        }

        var request = new FinixIdentityRequest
        {
            Entity = entity,
            IdentityRoles = new List<string> { "BUYER" },
            Type = "PERSONAL",
            Tags = new Dictionary<string, string> { ["source"] = "eshoponweb" }
        };

        return await SendAsync<FinixIdentityRequest, FinixIdentityResponse>(HttpMethod.Post, "/identities",
            request, cancellationToken);
    }

    public async Task<FinixPaymentInstrumentResponse> CreatePaymentInstrumentAsync(FinixCardRequest card,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(card.Identity))
        {
            var identity = await CreateBuyerIdentityAsync(null, card.Name, cancellationToken);
            card.Identity = identity.Id;
        }

        return await SendAsync<FinixCardRequest, FinixPaymentInstrumentResponse>(HttpMethod.Post,
            "/payment_instruments", card, cancellationToken);
    }

    public Task<FinixAuthorizationResponse> CreateAuthorizationAsync(long amountCents, string currency, string source,
        string idempotencyId, IReadOnlyDictionary<string, string>? tags, CancellationToken cancellationToken = default)
    {
        var options = GetOptions();
        var request = new FinixAuthorizationRequest
        {
            Amount = amountCents,
            Currency = currency,
            Merchant = options.MerchantId!,
            Source = source,
            IdempotencyId = idempotencyId,
            Tags = tags == null ? null : new Dictionary<string, string>(tags)
        };

        return SendAsync<FinixAuthorizationRequest, FinixAuthorizationResponse>(HttpMethod.Post, "/authorizations",
            request, cancellationToken);
    }

    public Task<FinixAuthorizationResponse> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<object, FinixAuthorizationResponse>(HttpMethod.Get,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}", null, cancellationToken);
    }

    public Task<FinixAuthorizationResponse> CaptureAuthorizationAsync(string authorizationId, long captureAmountCents,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixCaptureRequest, FinixAuthorizationResponse>(HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            new FinixCaptureRequest { CaptureAmount = captureAmountCents }, cancellationToken);
    }

    public Task<FinixAuthorizationResponse> VoidAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixVoidRequest, FinixAuthorizationResponse>(HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            new FinixVoidRequest { VoidMe = true }, cancellationToken);
    }

    public Task<FinixTransferResponse> GetTransferAsync(string transferId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<object, FinixTransferResponse>(HttpMethod.Get,
            $"/transfers/{Uri.EscapeDataString(transferId)}", null, cancellationToken);
    }

    public Task<FinixTransferResponse> RefundTransferAsync(string transferId, long refundAmountCents,
        string idempotencyId, IReadOnlyDictionary<string, string>? tags, CancellationToken cancellationToken = default)
    {
        var request = new FinixRefundRequest
        {
            RefundAmount = refundAmountCents,
            IdempotencyId = idempotencyId,
            Tags = tags == null ? null : new Dictionary<string, string>(tags)
        };

        return SendAsync<FinixRefundRequest, FinixTransferResponse>(HttpMethod.Post,
            $"/transfers/{Uri.EscapeDataString(transferId)}/reversals", request, cancellationToken);
    }

    public async Task<IReadOnlyList<FinixTransferResponse>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FinixTransferResponse>();
        var path = BuildListPath("/transfers", from, to, null);
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);

        while (true)
        {
            var page = await SendAsync<object, FinixTransferListResponse>(HttpMethod.Get, path, null, cancellationToken);
            if (page.Embedded?.Transfers != null)
            {
                results.AddRange(page.Embedded.Transfers);
            }

            var cursor = page.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor) || !seenCursors.Add(cursor))
            {
                break;
            }

            path = BuildListPath("/transfers", from, to, cursor);
        }

        return results;
    }

    public async Task<IReadOnlyList<FinixAuthorizationResponse>> ListAuthorizationsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<FinixAuthorizationResponse>();
        var path = BuildListPath("/authorizations", from, to, null);
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);

        while (true)
        {
            var page = await SendAsync<object, FinixAuthorizationListResponse>(HttpMethod.Get, path, null,
                cancellationToken);
            if (page.Embedded?.Authorizations != null)
            {
                results.AddRange(page.Embedded.Authorizations);
            }

            var cursor = page.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor) || !seenCursors.Add(cursor))
            {
                break;
            }

            path = BuildListPath("/authorizations", from, to, cursor);
        }

        return results;
    }

    private static string BuildListPath(string resource, DateTimeOffset from, DateTimeOffset to, string? cursor)
    {
        var fromValue = Uri.EscapeDataString(from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        var toValue = Uri.EscapeDataString(to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        var path = $"{resource}?created_at.gte={fromValue}&created_at.lte={toValue}&limit={PageLimit}";
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            path += $"&after_cursor={Uri.EscapeDataString(cursor)}";
        }

        return path;
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(HttpMethod method, string path, TRequest? body,
        CancellationToken cancellationToken)
    {
        var options = GetOptions();
        var baseUrl = options.ResolveBaseUrl();
        var uri = new Uri($"{baseUrl}{path}");

        using var request = new HttpRequestMessage(method, uri);
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{options.Username}:{options.Password}"));
        request.Headers.TryAddWithoutValidation("Authorization", $"Basic {credentials}");
        request.Headers.TryAddWithoutValidation("Finix-Version", FinixVersion);

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("Calling Finix {Method} {Path}", method.Method, path);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, responseBody);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            throw new FinixApiException((int)response.StatusCode, "Finix returned an empty response.");
        }

        var result = JsonSerializer.Deserialize<TResponse>(responseBody, JsonOptions);
        if (result == null)
        {
            throw new FinixApiException((int)response.StatusCode, "Unable to read the Finix response.");
        }

        return result;
    }

    private FinixOptions GetOptions()
    {
        var options = _options.Value;
        options.EnsureConfigured();
        return options;
    }

    private static FinixApiException BuildException(HttpStatusCode statusCode, string responseBody)
    {
        string? message = null;
        string? code = null;
        string? duplicateId = null;

        try
        {
            var envelope = JsonSerializer.Deserialize<FinixErrorEnvelope>(responseBody, JsonOptions);
            var error = envelope?.Embedded?.Errors?.Count > 0 ? envelope.Embedded.Errors[0] : null;
            if (error != null)
            {
                message = error.Message;
                code = error.Code;
                duplicateId = error.Authorization ?? error.Transfer ?? error.Id;
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw body below.
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            message = string.IsNullOrWhiteSpace(responseBody)
                ? $"Finix returned HTTP {(int)statusCode}."
                : responseBody;
        }

        return new FinixApiException((int)statusCode, message!, code, duplicateId, responseBody);
    }
}
