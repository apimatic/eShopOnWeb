using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Finix;

/// <summary>
/// Hand-written client for the Finix API. The contract is finix-spec/finix-openapi.yaml.
/// No Finix SDK is used.
/// </summary>
public class FinixClient : IFinixClient
{
    private const string FinixVersion = "2022-02-01";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly FinixOptions _options;

    public FinixClient(HttpClient httpClient, IOptions<FinixOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<FinixIdentity> CreateBuyerIdentityAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var body = new FinixIdentityRequest
        {
            Entity = new { },
            IdentityRoles = new[] { "BUYER" },
            Type = "PERSONAL",
            Tags = new Dictionary<string, string> { ["eshop_buyer_id"] = buyerId }
        };

        return await SendAsync<FinixIdentity>(HttpMethod.Post, "identities", body, cancellationToken);
    }

    public async Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(string identityId, string buyerId,
        CardDetails card, CancellationToken cancellationToken = default)
    {
        var body = new FinixCreatePaymentInstrumentRequest
        {
            Identity = identityId,
            Name = card.Name,
            Number = card.Number,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            SecurityCode = card.SecurityCode,
            Address = card.Address is null
                ? null
                : new FinixAddress
                {
                    Line1 = card.Address.Line1,
                    Line2 = card.Address.Line2,
                    City = card.Address.City,
                    Region = card.Address.Region,
                    PostalCode = card.Address.PostalCode,
                    Country = card.Address.Country
                },
            Tags = new Dictionary<string, string> { ["eshop_buyer_id"] = buyerId }
        };

        return await SendAsync<FinixPaymentInstrument>(HttpMethod.Post, "payment_instruments", body,
            cancellationToken);
    }

    public async Task<FinixAuthorization> CreateAuthorizationAsync(string sourcePaymentInstrumentId,
        long amountCents, string? securityCode, string idempotencyId,
        IReadOnlyDictionary<string, string> tags, CancellationToken cancellationToken = default)
    {
        var body = new FinixCreateAuthorizationRequest
        {
            Amount = amountCents,
            Currency = _options.ResolveCurrency(),
            Merchant = _options.MerchantId,
            Source = sourcePaymentInstrumentId,
            SecurityCode = securityCode,
            IdempotencyId = idempotencyId,
            Tags = tags.ToDictionary(k => k.Key, v => v.Value)
        };

        return await SendAsync<FinixAuthorization>(HttpMethod.Post, "authorizations", body, cancellationToken);
    }

    public async Task<FinixAuthorization> CaptureAuthorizationAsync(string authorizationId, long amountCents,
        CancellationToken cancellationToken = default)
    {
        var body = new FinixCaptureAuthorizationRequest { CaptureAmount = amountCents };
        return await SendAsync<FinixAuthorization>(HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}", body, cancellationToken);
    }

    public async Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var body = new FinixVoidAuthorizationRequest { VoidMe = true };
        return await SendAsync<FinixAuthorization>(HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}", body, cancellationToken);
    }

    public async Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync<FinixAuthorization>(HttpMethod.Get,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}", null, cancellationToken);
    }

    public async Task<FinixTransfer> GetTransferAsync(string transferId,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync<FinixTransfer>(HttpMethod.Get,
            $"transfers/{Uri.EscapeDataString(transferId)}", null, cancellationToken);
    }

    public async Task<IReadOnlyList<FinixFee>> ListFeesForTransferAsync(string transferId,
        CancellationToken cancellationToken = default)
    {
        var path = $"fees?linked_to={Uri.EscapeDataString(transferId)}&limit=100";
        return await ReadAllAsync<FinixFee>(path, "fees", cancellationToken);
    }

    public async Task<FinixTransfer> CreateRefundAsync(string captureTransferId, long amountCents,
        CancellationToken cancellationToken = default)
    {
        var body = new FinixCreateReversalRequest { RefundAmount = amountCents };
        return await SendAsync<FinixTransfer>(HttpMethod.Post,
            $"transfers/{Uri.EscapeDataString(captureTransferId)}/reversals", body, cancellationToken);
    }

    public async Task DisablePaymentInstrumentAsync(string paymentInstrumentId,
        CancellationToken cancellationToken = default)
    {
        var body = new FinixDisablePaymentInstrumentRequest { Enabled = false };
        await SendAsync<FinixPaymentInstrument>(HttpMethod.Put,
            $"payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}", body, cancellationToken);
    }

    public async Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var path = "transfers?created_at.gte=" + Uri.EscapeDataString(FormatDate(from)) +
                   "&created_at.lte=" + Uri.EscapeDataString(FormatDate(to)) +
                   "&merchant=" + Uri.EscapeDataString(_options.MerchantId) +
                   "&limit=100";
        return await ReadAllAsync<FinixTransfer>(path, "transfers", cancellationToken);
    }

    public async Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var path = "authorizations?created_at.gte=" + Uri.EscapeDataString(FormatDate(from)) +
                   "&created_at.lte=" + Uri.EscapeDataString(FormatDate(to)) +
                   "&merchant=" + Uri.EscapeDataString(_options.MerchantId) +
                   "&limit=100";
        return await ReadAllAsync<FinixAuthorization>(path, "authorizations", cancellationToken);
    }

    private async Task<IReadOnlyList<T>> ReadAllAsync<T>(string initialPath, string embeddedKey,
        CancellationToken cancellationToken)
    {
        var results = new List<T>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? next = initialPath;

        while (!string.IsNullOrWhiteSpace(next))
        {
            if (!visited.Add(next))
            {
                break;
            }

            var page = await SendAsync<FinixListResponse<T>>(HttpMethod.Get, next, null, cancellationToken);

            if (page.Embedded is not null && page.Embedded.TryGetValue(embeddedKey, out var items) &&
                items is not null)
            {
                results.AddRange(items);
            }

            if (!string.IsNullOrWhiteSpace(page.Links?.Next?.Href))
            {
                next = page.Links.Next.Href;
            }
            else if (!string.IsNullOrWhiteSpace(page.Page?.NextCursor))
            {
                next = AppendAfterCursor(initialPath, page.Page.NextCursor);
            }
            else
            {
                next = null;
            }
        }

        return results;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body,
        CancellationToken cancellationToken)
    {
        _options.EnsureConfigured();

        using var request = new HttpRequestMessage(method, BuildRequestUri(path));
        AddHeaders(request);

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions)
               ?? throw PaymentException.Upstream("Finix returned an empty or invalid response.");
    }

    private Uri BuildRequestUri(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
        {
            return ResolveAbsoluteUri(absolute);
        }

        var baseAddress = _httpClient.BaseAddress
                          ?? new Uri(_options.ResolveBaseUrl(), UriKind.Absolute);
        return new Uri(baseAddress, path);
    }

    private Uri ResolveAbsoluteUri(Uri absolute)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return absolute;
        }

        var configuredBase = new Uri(_options.ResolveBaseUrl(), UriKind.Absolute);
        var builder = new UriBuilder(configuredBase)
        {
            Path = absolute.AbsolutePath,
            Query = absolute.Query.TrimStart('?')
        };
        return builder.Uri;
    }

    private static string AppendAfterCursor(string path, string cursor)
    {
        var separator = path.Contains('?') ? "&" : "?";
        return path + separator + "after_cursor=" + Uri.EscapeDataString(cursor);
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss");

    private void AddHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("Finix-Version", FinixVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{_options.Username}:{_options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    private static PaymentException BuildException(HttpStatusCode statusCode, string content)
    {
        var code = "FINIX_REQUEST_FAILED";
        var message = $"Finix returned HTTP {(int)statusCode}.";

        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                var error = JsonSerializer.Deserialize<FinixErrorEnvelope>(content, JsonOptions);
                var first = error?.Embedded?.Errors?.FirstOrDefault();
                if (first is not null)
                {
                    code = string.IsNullOrWhiteSpace(first.Code) ? code : first.Code!;
                    message = string.IsNullOrWhiteSpace(first.Message)
                        ? $"{code} (Finix HTTP {(int)statusCode})."
                        : $"{code}: {first.Message}";
                }
            }
            catch (JsonException)
            {
                // Do not echo arbitrary upstream content: it could contain request data.
            }
        }

        var status = (int)statusCode;
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ||
            status >= 500)
        {
            return new PaymentException(
                $"Finix rejected the request ({code}). Check the configured Finix credentials, merchant id and environment. {message}",
                502, "FINIX_UPSTREAM_ERROR");
        }

        return new PaymentException(message, status, code);
    }
}
