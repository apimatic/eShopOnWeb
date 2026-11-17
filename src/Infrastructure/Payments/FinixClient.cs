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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class FinixClient : IFinixClient
{
    private const string FinixVersion = "2022-02-01";
    private const int PageSize = 100;
    private const int MaxPages = 10_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixClient> _logger;

    public FinixClient(HttpClient httpClient, IOptions<FinixOptions> options, ILogger<FinixClient> logger)
    {
        _options = options.Value;
        _options.EnsureConfigured();
        _logger = logger;

        var baseUrl = _options.ResolveBaseUrl();
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}")));
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Remove("Finix-Version");
        _httpClient.DefaultRequestHeaders.Add("Finix-Version", FinixVersion);
    }

    public Task<FinixAuthorization> CreateAuthorizationAsync(
        long amountInCents,
        string currency,
        string source,
        string idempotencyId,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixCreateAuthorizationRequest
        {
            Amount = amountInCents,
            Currency = currency,
            Merchant = _options.MerchantId!,
            Source = source,
            IdempotencyId = idempotencyId,
            Tags = tags
        };

        return SendAsync<FinixCreateAuthorizationRequest, FinixAuthorization>(
            HttpMethod.Post, "authorizations", request, cancellationToken);
    }

    public Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        return SendAsync<object, FinixAuthorization>(
            HttpMethod.Get, $"authorizations/{Uri.EscapeDataString(authorizationId)}", null, cancellationToken);
    }

    public Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId,
        long captureAmountInCents,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixCaptureAuthorizationRequest { CaptureAmount = captureAmountInCents };
        return SendAsync<FinixCaptureAuthorizationRequest, FinixAuthorization>(
            HttpMethod.Put, $"authorizations/{Uri.EscapeDataString(authorizationId)}", request, cancellationToken);
    }

    public Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var request = new FinixVoidAuthorizationRequest { VoidMe = true };
        return SendAsync<FinixVoidAuthorizationRequest, FinixAuthorization>(
            HttpMethod.Put, $"authorizations/{Uri.EscapeDataString(authorizationId)}", request, cancellationToken);
    }

    public Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken = default)
    {
        return SendAsync<object, FinixTransfer>(
            HttpMethod.Get, $"transfers/{Uri.EscapeDataString(transferId)}", null, cancellationToken);
    }

    public Task<FinixTransfer> CreateReversalAsync(
        string transferId,
        long refundAmountInCents,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixCreateReversalRequest { RefundAmount = refundAmountInCents };
        return SendAsync<FinixCreateReversalRequest, FinixTransfer>(
            HttpMethod.Post, $"transfers/{Uri.EscapeDataString(transferId)}/reversals", request, cancellationToken);
    }

    public async Task<FinixIdentity> CreateBuyerIdentityAsync(
        string email,
        string? name,
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        var (firstName, lastName) = SplitName(name, email);
        var request = new FinixCreateIdentityRequest
        {
            Type = "PERSONAL",
            IdentityRoles = new List<string> { "BUYER" },
            Entity = new FinixIdentityEntity
            {
                Email = email,
                FirstName = firstName,
                LastName = lastName
            },
            Tags = new Dictionary<string, string>
            {
                ["source"] = "eshop-onweb",
                ["owner"] = Truncate(ownerId, 500)
            }
        };

        return await SendAsync<FinixCreateIdentityRequest, FinixIdentity>(
            HttpMethod.Post, "identities", request, cancellationToken);
    }

    public Task<FinixPaymentInstrument> CreateCardPaymentInstrumentAsync(
        CardDetails card,
        string identityId,
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixCreatePaymentInstrumentRequest
        {
            Type = "PAYMENT_CARD",
            Identity = identityId,
            Name = card.Name,
            Number = NormalizeCardNumber(card.Number),
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            SecurityCode = string.IsNullOrWhiteSpace(card.SecurityCode) ? null : card.SecurityCode.Trim(),
            Address = BuildAddress(card),
            Tags = new Dictionary<string, string>
            {
                ["source"] = "eshop-onweb",
                ["owner"] = Truncate(ownerId, 500)
            }
        };

        return SendAsync<FinixCreatePaymentInstrumentRequest, FinixPaymentInstrument>(
            HttpMethod.Post, "payment_instruments", request, cancellationToken);
    }

    public Task<FinixPaymentInstrument> DisablePaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixDisablePaymentInstrumentRequest { Enabled = false };
        return SendAsync<FinixDisablePaymentInstrumentRequest, FinixPaymentInstrument>(
            HttpMethod.Put, $"payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}", request, cancellationToken);
    }

    public Task<IReadOnlyList<FinixFee>> ListFeesForTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default)
    {
        var path = $"fees?merchant_id={Uri.EscapeDataString(_options.MerchantId!)}&linked_to={Uri.EscapeDataString(transferId)}";
        return ListAllAsync<FinixFeeList, FinixFee>(
            path,
            list => list.Embedded?.Fees ?? new List<FinixFee>(),
            list => list.Page,
            fee => fee.Id,
            cancellationToken);
    }

    public Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var path = $"authorizations?merchant={Uri.EscapeDataString(_options.MerchantId!)}" +
                   $"&created_at.gte={FormatFinixDate(from)}&created_at.lte={FormatFinixDate(to)}";
        return ListAllAsync<FinixAuthorizationList, FinixAuthorization>(
            path,
            list => list.Embedded?.Authorizations ?? new List<FinixAuthorization>(),
            list => list.Page,
            authorization => authorization.Id,
            cancellationToken);
    }

    public Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var path = $"transfers?merchant={Uri.EscapeDataString(_options.MerchantId!)}" +
                   $"&created_at.gte={FormatFinixDate(from)}&created_at.lte={FormatFinixDate(to)}";
        return ListAllAsync<FinixTransferList, FinixTransfer>(
            path,
            list => list.Embedded?.Transfers ?? new List<FinixTransfer>(),
            list => list.Page,
            transfer => transfer.Id,
            cancellationToken);
    }

    private async Task<IReadOnlyList<TItem>> ListAllAsync<TList, TItem>(
        string path,
        Func<TList, List<TItem>> getItems,
        Func<TList, FinixPage?> getPage,
        Func<TItem, string> getId,
        CancellationToken cancellationToken)
        where TList : class
    {
        var results = new List<TItem>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        string? afterCursor = null;

        for (var pageNumber = 0; pageNumber < MaxPages; pageNumber++)
        {
            var pagePath = path + $"&limit={PageSize}";
            if (!string.IsNullOrWhiteSpace(afterCursor))
            {
                pagePath += $"&after_cursor={Uri.EscapeDataString(afterCursor)}";
            }

            var page = await SendAsync<object, TList>(HttpMethod.Get, pagePath, null, cancellationToken);
            var items = getItems(page);

            foreach (var item in items)
            {
                if (seenIds.Add(getId(item)))
                {
                    results.Add(item);
                }
            }

            var nextCursor = getPage(page)?.NextCursor;
            if (items.Count == 0 ||
                string.IsNullOrWhiteSpace(nextCursor) ||
                string.Equals(nextCursor, afterCursor, StringComparison.Ordinal))
            {
                break;
            }

            afterCursor = nextCursor;
        }

        return results;
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string relativePath,
        TRequest? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("Calling Finix {Method} {Path}", method.Method, relativePath.Split('?')[0]);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (message, code) = ParseError(content);
            _logger.LogWarning("Finix {Method} {Path} returned {StatusCode} ({Code}): {Message}",
                method.Method, relativePath.Split('?')[0], (int)response.StatusCode, code, message);
            throw new FinixApiException((int)response.StatusCode, message, code, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new FinixApiException((int)HttpStatusCode.BadGateway, "Finix returned an empty response.");
        }

        var result = JsonSerializer.Deserialize<TResponse>(content, JsonOptions);
        if (result is null)
        {
            throw new FinixApiException((int)HttpStatusCode.BadGateway, "Finix returned an unreadable response.");
        }

        return result;
    }

    private static (string Message, string? Code) ParseError(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return ("Finix rejected the request without an error message.", null);
        }

        try
        {
            var error = JsonSerializer.Deserialize<FinixGenericError>(content, JsonOptions);
            var first = error?.Embedded?.Errors.FirstOrDefault();
            if (first is not null && !string.IsNullOrWhiteSpace(first.Message))
            {
                return (first.Message!, first.Code);
            }
        }
        catch (JsonException)
        {
            // Fall through to a generic message.
        }

        return ("Finix rejected the request.", null);
    }

    private static string FormatFinixDate(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss");
    }

    private static FinixAddress? BuildAddress(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Line1) ||
            string.IsNullOrWhiteSpace(card.City) ||
            string.IsNullOrWhiteSpace(card.Region) ||
            string.IsNullOrWhiteSpace(card.PostalCode) ||
            string.IsNullOrWhiteSpace(card.Country))
        {
            return null;
        }

        return new FinixAddress
        {
            Line1 = card.Line1,
            Line2 = card.Line2,
            City = card.City,
            Region = card.Region,
            PostalCode = card.PostalCode,
            Country = card.Country
        };
    }

    private static string NormalizeCardNumber(string number)
    {
        return (number ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Trim();
    }

    private static (string? FirstName, string? LastName) SplitName(string? name, string email)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            var localPart = email.Split('@')[0];
            return (Truncate(localPart, 20), null);
        }

        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return (Truncate(parts[0], 20), null);
        }

        return (Truncate(parts[0], 20), Truncate(parts[^1], 20));
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
