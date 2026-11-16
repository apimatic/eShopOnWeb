using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Hand-written client for the Finix API. Every request and response shape here is derived
/// from finix-spec/finix-openapi.yaml; no third-party Finix SDK is used.
/// </summary>
public sealed class FinixApiClient : IFinixGateway
{
    private const string FinixVersion = "2022-02-01";
    private const int PageSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly FinixOptions _options;
    private readonly IAppLogger<FinixApiClient> _logger;

    public FinixApiClient(HttpClient httpClient, FinixOptions options, IAppLogger<FinixApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl().TrimEnd('/') + "/");
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("Finix-Version"))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Finix-Version", FinixVersion);
        }

        if (_httpClient.DefaultRequestHeaders.Authorization is null)
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public async Task<string> CreateBuyerIdentityAsync(string email, string? firstName, string? lastName,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixCreateIdentityRequest
        {
            Entity = new FinixIdentityEntity
            {
                Email = email,
                FirstName = string.IsNullOrWhiteSpace(firstName) ? "eShop" : firstName,
                LastName = string.IsNullOrWhiteSpace(lastName) ? "Shopper" : lastName
            },
            IdentityRoles = new[] { "BUYER" },
            Type = "PERSONAL"
        };

        var response = await PostAsync<FinixIdentityResponse>("identities", request, cancellationToken);
        return response.Id;
    }

    public async Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(string identityId, CardDetails card,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixCreatePaymentInstrumentRequest
        {
            Type = "PAYMENT_CARD",
            Identity = identityId,
            Name = card.Name,
            Number = card.Number.Replace(" ", string.Empty).Replace("-", string.Empty),
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            SecurityCode = card.SecurityCode,
            Address = card.Address is null ? null : ToAddressBody(card.Address)
        };

        var response = await PostAsync<FinixPaymentInstrumentResponse>("payment_instruments", request, cancellationToken);
        return new FinixPaymentInstrument
        {
            Id = response.Id,
            Brand = response.Brand,
            LastFour = response.LastFour,
            ExpirationMonth = response.ExpirationMonth,
            ExpirationYear = response.ExpirationYear,
            Name = response.Name,
            Enabled = response.Enabled
        };
    }

    public async Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken = default)
    {
        await PutAsync<FinixDisablePaymentInstrumentRequest, FinixPaymentInstrumentResponse>(
            $"payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            new FinixDisablePaymentInstrumentRequest { Enabled = false },
            cancellationToken);
    }

    public async Task<FinixAuthorization> CreateAuthorizationAsync(long amountCents, string currency,
        string paymentInstrumentId, string orderReference, string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixCreateAuthorizationRequest
        {
            Amount = amountCents,
            Currency = currency,
            Merchant = _options.MerchantId,
            Source = paymentInstrumentId,
            IdempotencyId = idempotencyId,
            Tags = new Dictionary<string, string> { ["eshop_order"] = orderReference }
        };

        var response = await PostAsync<FinixAuthorizationResponse>("authorizations", request, cancellationToken);
        return ToAuthorization(response);
    }

    public async Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<FinixAuthorizationResponse>(
            $"authorizations/{Uri.EscapeDataString(authorizationId)}", cancellationToken);
        return ToAuthorization(response);
    }

    public async Task<FinixAuthorization> CaptureAuthorizationAsync(string authorizationId, long captureAmountCents,
        CancellationToken cancellationToken = default)
    {
        var response = await PutAsync<FinixCaptureAuthorizationRequest, FinixAuthorizationResponse>(
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            new FinixCaptureAuthorizationRequest { CaptureAmount = captureAmountCents },
            cancellationToken);
        return ToAuthorization(response);
    }

    public async Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var response = await PutAsync<FinixVoidAuthorizationRequest, FinixAuthorizationResponse>(
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            new FinixVoidAuthorizationRequest { VoidMe = true },
            cancellationToken);
        return ToAuthorization(response);
    }

    public async Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<FinixTransferResponse>(
            $"transfers/{Uri.EscapeDataString(transferId)}", cancellationToken);
        return ToTransfer(response);
    }

    public async Task<FinixTransfer> CreateRefundAsync(string transferId, long refundAmountCents,
        CancellationToken cancellationToken = default)
    {
        var response = await PostAsync<FinixCreateRefundRequest, FinixTransferResponse>(
            $"transfers/{Uri.EscapeDataString(transferId)}/reversals",
            new FinixCreateRefundRequest { RefundAmount = refundAmountCents },
            cancellationToken);
        return ToTransfer(response);
    }

    public async Task<IReadOnlyList<FinixFee>> ListFeesForTransferAsync(string transferId,
        CancellationToken cancellationToken = default)
    {
        var url = $"fees?linked_to={Uri.EscapeDataString(transferId)}&limit={PageSize}";
        var responses = await GetAllPagesAsync<FinixFeeListResponse, FinixFeeResponse>(
            url,
            response => response.Embedded?.Fees,
            GetNextCursor,
            cancellationToken);

        return responses.Select(fee => new FinixFee
        {
            Id = fee.Id,
            Amount = fee.Amount,
            Currency = fee.Currency,
            FeeType = fee.FeeType,
            FeeSubtype = fee.FeeSubtype,
            LinkedTo = fee.LinkedTo,
            Category = fee.Category,
            CreatedAt = fee.CreatedAt
        }).ToList();
    }

    public async Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var url = $"transfers?created_at.gte={FormatDate(from)}&created_at.lte={FormatDate(to)}&limit={PageSize}";
        var responses = await GetAllPagesAsync<FinixTransferListResponse, FinixTransferResponse>(
            url,
            response => response.Embedded?.Transfers,
            GetNextCursor,
            cancellationToken);

        return responses.Select(ToTransfer).ToList();
    }

    public async Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var url = $"authorizations?created_at.gte={FormatDate(from)}&created_at.lte={FormatDate(to)}&limit={PageSize}";
        var responses = await GetAllPagesAsync<FinixAuthorizationListResponse, FinixAuthorizationResponse>(
            url,
            response => response.Embedded?.Authorizations,
            GetNextCursor,
            cancellationToken);

        return responses.Select(ToAuthorization).ToList();
    }

    private async Task<List<TItem>> GetAllPagesAsync<TResponse, TItem>(string url,
        Func<TResponse, List<TItem>?> itemsSelector,
        Func<TResponse, string?> cursorSelector,
        CancellationToken cancellationToken)
    {
        var items = new List<TItem>();
        string? cursor = null;

        for (var page = 0; page < 10_000; page++)
        {
            var pageUrl = url;
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                pageUrl += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await GetAsync<TResponse>(pageUrl, cancellationToken);
            var pageItems = itemsSelector(response);
            if (pageItems is not null)
            {
                items.AddRange(pageItems);
            }

            cursor = cursorSelector(response);
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        return items;
    }

    private static string? GetNextCursor<TResponse>(TResponse response)
    {
        return response switch
        {
            FinixTransferListResponse transferList => transferList.Page?.NextCursor ?? ExtractCursor(transferList.Links?.Next?.Href),
            FinixAuthorizationListResponse authorizationList => authorizationList.Page?.NextCursor ?? ExtractCursor(authorizationList.Links?.Next?.Href),
            FinixFeeListResponse feeList => feeList.Page?.NextCursor ?? ExtractCursor(feeList.Links?.Next?.Href),
            _ => null
        };
    }

    private static string? ExtractCursor(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        const string marker = "after_cursor=";
        var index = href.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var value = href[(index + marker.Length)..];
        var ampersand = value.IndexOf('&');
        if (ampersand >= 0)
        {
            value = value[..ampersand];
        }

        return Uri.UnescapeDataString(value);
    }

    private async Task<TResponse> GetAsync<TResponse>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        return await SendAsync<TResponse>(request, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TResponse>(string relativeUrl, object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        return await SendAsync<TResponse>(request, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest body,
        CancellationToken cancellationToken)
    {
        return await PostAsync<TResponse>(relativeUrl, body!, cancellationToken);
    }

    private async Task<TResponse> PutAsync<TRequest, TResponse>(string relativeUrl, TRequest body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, relativeUrl)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        return await SendAsync<TResponse>(request, cancellationToken);
    }

    private async Task<TResponse> SendAsync<TResponse>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, responseBody);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            throw new FinixApiException((int)response.StatusCode, "EMPTY_RESPONSE",
                $"Finix returned an empty response for {request.Method} {request.RequestUri}.");
        }

        var result = JsonSerializer.Deserialize<TResponse>(responseBody, JsonOptions);
        if (result is null)
        {
            throw new FinixApiException((int)response.StatusCode, "INVALID_RESPONSE",
                $"Finix returned a response that could not be read for {request.Method} {request.RequestUri}.");
        }

        return result;
    }

    private static FinixApiException BuildException(HttpStatusCode statusCode, string responseBody)
    {
        var code = (string?)null;
        var message = $"Finix returned HTTP {(int)statusCode}.";

        try
        {
            var error = JsonSerializer.Deserialize<FinixErrorEnvelope>(responseBody, JsonOptions);
            var firstError = error?.Embedded?.Errors?.FirstOrDefault();
            if (firstError is not null)
            {
                code = firstError.Code;
                if (!string.IsNullOrWhiteSpace(firstError.Message))
                {
                    message = firstError.Message;
                }
            }
        }
        catch (JsonException)
        {
            // Preserve the generic message; the raw body is kept for diagnostics but never
            // contains card details because it is Finix's response, not our request.
        }

        return new FinixApiException((int)statusCode, code, message, responseBody);
    }

    private static FinixAuthorization ToAuthorization(FinixAuthorizationResponse response)
    {
        return new FinixAuthorization
        {
            Id = response.Id,
            State = response.State,
            TransferId = response.TransferId,
            IsVoid = response.IsVoid,
            VoidState = response.VoidState,
            Amount = response.Amount,
            AmountRequested = response.AmountRequested,
            Currency = response.Currency,
            Source = response.Source,
            FailureCode = response.FailureCode,
            FailureMessage = response.FailureMessage,
            ExpiresAt = response.ExpiresAt,
            CreatedAt = response.CreatedAt,
            Merchant = response.Merchant
        };
    }

    private static FinixTransfer ToTransfer(FinixTransferResponse response)
    {
        return new FinixTransfer
        {
            Id = response.Id,
            State = response.State,
            Type = response.Type,
            OperationKey = response.OperationKey,
            Amount = response.Amount,
            AmountRequested = response.AmountRequested,
            Currency = response.Currency,
            Source = response.Source,
            ParentTransfer = response.ParentTransfer,
            FailureCode = response.FailureCode,
            FailureMessage = response.FailureMessage,
            CreatedAt = response.CreatedAt,
            UpdatedAt = response.UpdatedAt,
            Fee = response.Fee,
            Merchant = response.Merchant,
            Tags = response.Tags
        };
    }

    private static FinixAddressBody ToAddressBody(FinixAddress address)
    {
        return new FinixAddressBody
        {
            Line1 = address.Line1,
            Line2 = address.Line2,
            City = address.City,
            Region = address.Region,
            PostalCode = address.PostalCode,
            Country = NormalizeCountry(address.Country)
        };
    }

    private static string NormalizeCountry(string? country)
    {
        return (country ?? "USA").Trim().ToUpperInvariant() switch
        {
            "US" => "USA",
            "CA" => "CAN",
            var value when value.Length == 0 => "USA",
            var value => value
        };
    }

    private static string FormatDate(DateTimeOffset value)
    {
        return Uri.EscapeDataString(value.ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }

    private sealed class FinixCreateIdentityRequest
    {
        public FinixIdentityEntity Entity { get; set; } = new();
        public string[] IdentityRoles { get; set; } = Array.Empty<string>();
        public string Type { get; set; } = "PERSONAL";
    }

    private sealed class FinixIdentityEntity
    {
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }

    private sealed class FinixIdentityResponse
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class FinixCreatePaymentInstrumentRequest
    {
        public string Type { get; set; } = "PAYMENT_CARD";
        public string Identity { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public int ExpirationMonth { get; set; }
        public int ExpirationYear { get; set; }
        public string? SecurityCode { get; set; }
        public FinixAddressBody? Address { get; set; }
    }

    private sealed class FinixAddressBody
    {
        public string Line1 { get; set; } = string.Empty;
        public string? Line2 { get; set; }
        public string City { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string Country { get; set; } = "USA";
    }

    private sealed class FinixPaymentInstrumentResponse
    {
        public string Id { get; set; } = string.Empty;
        public string? Brand { get; set; }
        [JsonPropertyName("last_four")]
        public string? LastFour { get; set; }
        public int? ExpirationMonth { get; set; }
        public int? ExpirationYear { get; set; }
        public string? Name { get; set; }
        public bool? Enabled { get; set; }
    }

    private sealed class FinixDisablePaymentInstrumentRequest
    {
        public bool Enabled { get; set; }
    }

    private sealed class FinixCreateAuthorizationRequest
    {
        public long Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Merchant { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string? IdempotencyId { get; set; }
        public Dictionary<string, string>? Tags { get; set; }
    }

    private sealed class FinixCaptureAuthorizationRequest
    {
        public long CaptureAmount { get; set; }
    }

    private sealed class FinixVoidAuthorizationRequest
    {
        public bool VoidMe { get; set; }
    }

    private sealed class FinixCreateRefundRequest
    {
        public long RefundAmount { get; set; }
    }

    private sealed class FinixAuthorizationResponse
    {
        public string Id { get; set; } = string.Empty;
        public string? State { get; set; }
        [JsonPropertyName("transfer")]
        public string? TransferId { get; set; }
        public bool? IsVoid { get; set; }
        public string? VoidState { get; set; }
        public long? Amount { get; set; }
        public long? AmountRequested { get; set; }
        public string? Currency { get; set; }
        public string? Source { get; set; }
        public string? FailureCode { get; set; }
        public string? FailureMessage { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public string? Merchant { get; set; }
    }

    private sealed class FinixTransferResponse
    {
        public string Id { get; set; } = string.Empty;
        public string? State { get; set; }
        public string? Type { get; set; }
        public string? OperationKey { get; set; }
        public long? Amount { get; set; }
        public long? AmountRequested { get; set; }
        public string? Currency { get; set; }
        public string? Source { get; set; }
        public string? ParentTransfer { get; set; }
        public string? FailureCode { get; set; }
        public string? FailureMessage { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public long? Fee { get; set; }
        public string? Merchant { get; set; }
        public Dictionary<string, object?>? Tags { get; set; }
    }

    private sealed class FinixFeeResponse
    {
        public string Id { get; set; } = string.Empty;
        public decimal? Amount { get; set; }
        public string? Currency { get; set; }
        public string? FeeType { get; set; }
        public string? FeeSubtype { get; set; }
        public string? LinkedTo { get; set; }
        public string? Category { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
    }

    private sealed class FinixTransferListResponse
    {
        [JsonPropertyName("_embedded")]
        public FinixTransferEmbedded? Embedded { get; set; }
        [JsonPropertyName("_links")]
        public FinixLinks? Links { get; set; }
        public FinixPage? Page { get; set; }
    }

    private sealed class FinixTransferEmbedded
    {
        public List<FinixTransferResponse>? Transfers { get; set; }
    }

    private sealed class FinixAuthorizationListResponse
    {
        [JsonPropertyName("_embedded")]
        public FinixAuthorizationEmbedded? Embedded { get; set; }
        [JsonPropertyName("_links")]
        public FinixLinks? Links { get; set; }
        public FinixPage? Page { get; set; }
    }

    private sealed class FinixAuthorizationEmbedded
    {
        public List<FinixAuthorizationResponse>? Authorizations { get; set; }
    }

    private sealed class FinixFeeListResponse
    {
        [JsonPropertyName("_embedded")]
        public FinixFeeEmbedded? Embedded { get; set; }
        [JsonPropertyName("_links")]
        public FinixLinks? Links { get; set; }
        public FinixPage? Page { get; set; }
    }

    private sealed class FinixFeeEmbedded
    {
        public List<FinixFeeResponse>? Fees { get; set; }
    }

    private sealed class FinixPage
    {
        public int? Limit { get; set; }
        public string? NextCursor { get; set; }
    }

    private sealed class FinixLinks
    {
        public FinixLink? Next { get; set; }
    }

    private sealed class FinixLink
    {
        public string? Href { get; set; }
    }

    private sealed class FinixErrorEnvelope
    {
        public int Total { get; set; }
        [JsonPropertyName("_embedded")]
        public FinixErrorEmbedded? Embedded { get; set; }
    }

    private sealed class FinixErrorEmbedded
    {
        public List<FinixErrorItem>? Errors { get; set; }
    }

    private sealed class FinixErrorItem
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
        public string? Logref { get; set; }
    }
}
