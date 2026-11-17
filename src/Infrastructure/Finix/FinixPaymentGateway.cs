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
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public class FinixPaymentGateway : IFinixPaymentGateway
{
    private const string FinixVersion = "2022-02-01";
    private const string SandboxBaseUrl = "https://finix.sandbox-payments-api.com";
    private const string LiveBaseUrl = "https://finix.live-payments-api.com";

    private static readonly HttpClient HttpClient = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly FinixOptions _options;
    private readonly ILogger<FinixPaymentGateway> _logger;
    private string? _baseUrl;
    private readonly SemaphoreSlim _identityLock = new(1, 1);
    private string? _identityId;

    public FinixPaymentGateway(IOptions<FinixOptions> options, ILogger<FinixPaymentGateway> logger)
    {
        _options = options.Value;
        _logger = logger;
        _baseUrl = TryResolveBaseUrl(_options);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.Username)
            || string.IsNullOrWhiteSpace(_options.Password)
            || string.IsNullOrWhiteSpace(_options.MerchantId)
            || string.IsNullOrWhiteSpace(_options.Currency)
            || string.IsNullOrWhiteSpace(_options.Environment))
        {
            throw new InvalidOperationException(
                "Finix configuration is incomplete. Configure Finix:Username, Finix:Password, " +
                "Finix:MerchantId, Finix:Environment and Finix:Currency.");
        }

        _baseUrl ??= ResolveBaseUrl(_options);
    }

    public async Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(FinixCardDetails card, CancellationToken cancellationToken)
    {
        var identityId = await GetOrCreateIdentityAsync(cancellationToken);

        var body = new CreatePaymentInstrumentRequest
        {
            Type = "PAYMENT_CARD",
            Number = card.Number,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            Identity = identityId,
            Address = ToFinixAddress(card.Address)
        };

        var response = await SendAsync<PaymentInstrumentResponse>(
            HttpMethod.Post,
            "/payment_instruments",
            body,
            cancellationToken);

        return MapPaymentInstrument(response);
    }

    public async Task<FinixPaymentInstrument?> GetPaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAsync<PaymentInstrumentResponse>(
                HttpMethod.Get,
                $"/payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
                null,
                cancellationToken);
            return MapPaymentInstrument(response);
        }
        catch (FinixApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync<PaymentInstrumentResponse>(
                HttpMethod.Put,
                $"/payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
                new UpdatePaymentInstrumentRequest { Enabled = false },
                cancellationToken);
        }
        catch (FinixApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Finix payment instrument {PaymentInstrumentId} was already absent when disabling it.", paymentInstrumentId);
        }
    }

    public async Task<FinixAuthorization> CreateAuthorizationAsync(string paymentInstrumentId,
        long amountCents,
        string currency,
        string idempotencyId,
        string orderId,
        string orderKey,
        string buyerId,
        CancellationToken cancellationToken)
    {
        var body = new CreateAuthorizationRequest
        {
            Amount = amountCents,
            Currency = currency,
            Merchant = _options.MerchantId,
            Source = paymentInstrumentId,
            IdempotencyId = idempotencyId,
            Tags = new Dictionary<string, string>
            {
                ["eshop_order_id"] = orderId,
                ["eshop_order_key"] = orderKey,
                ["eshop_buyer_id"] = buyerId
            }
        };

        var response = await SendAsync<AuthorizationResponse>(
            HttpMethod.Post,
            "/authorizations",
            body,
            cancellationToken);

        return MapAuthorization(response);
    }

    public async Task<FinixAuthorization?> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAsync<AuthorizationResponse>(
                HttpMethod.Get,
                $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
                null,
                cancellationToken);
            return MapAuthorization(response);
        }
        catch (FinixApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<FinixAuthorization> CaptureAuthorizationAsync(string authorizationId, long amountCents, CancellationToken cancellationToken)
    {
        var response = await SendAsync<AuthorizationResponse>(
            HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            new CaptureAuthorizationRequest { CaptureAmount = amountCents },
            cancellationToken);
        return MapAuthorization(response);
    }

    public async Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<AuthorizationResponse>(
            HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            new VoidAuthorizationRequest { VoidMe = true },
            cancellationToken);
        return MapAuthorization(response);
    }

    public async Task<FinixTransfer?> GetTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAsync<TransferResponse>(
                HttpMethod.Get,
                $"/transfers/{Uri.EscapeDataString(transferId)}",
                null,
                cancellationToken);
            return MapTransfer(response);
        }
        catch (FinixApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<FinixTransfer> CreateRefundAsync(string captureTransferId, long amountCents, CancellationToken cancellationToken)
    {
        var response = await SendAsync<TransferResponse>(
            HttpMethod.Post,
            $"/transfers/{Uri.EscapeDataString(captureTransferId)}/reversals",
            new CreateRefundRequest { RefundAmount = amountCents },
            cancellationToken);
        return MapTransfer(response);
    }

    public async Task<IReadOnlyList<FinixTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var records = new List<FinixTransactionRecord>();

        var authorizationTransactions = await ListAllAsync<AuthorizationResponse, AuthorizationListResponse>(
            "/authorizations",
            from,
            to,
            response => response.Embedded?.Authorizations ?? new List<AuthorizationResponse>(),
            response => response.Page?.NextCursor,
            cancellationToken);

        foreach (var authorization in authorizationTransactions)
        {
            records.Add(MapAuthorizationTransaction(authorization));
        }

        var transferTransactions = await ListAllAsync<TransferResponse, TransferListResponse>(
            "/transfers",
            from,
            to,
            response => response.Embedded?.Transfers ?? new List<TransferResponse>(),
            response => response.Page?.NextCursor,
            cancellationToken);

        foreach (var transfer in transferTransactions)
        {
            records.Add(MapTransferTransaction(transfer));
        }

        return records;
    }

    private async Task<List<TItem>> ListAllAsync<TItem, TPage>(
        string path,
        DateTimeOffset from,
        DateTimeOffset to,
        Func<TPage, List<TItem>> itemsSelector,
        Func<TPage, string?> nextCursorSelector,
        CancellationToken cancellationToken)
        where TPage : class
    {
        var results = new List<TItem>();
        string? cursor = null;

        do
        {
            var query = new StringBuilder(path)
                .Append("?created_at.gte=").Append(Uri.EscapeDataString(FormatDate(from)))
                .Append("&created_at.lte=").Append(Uri.EscapeDataString(FormatDate(to)))
                .Append("&merchant=").Append(Uri.EscapeDataString(_options.MerchantId))
                .Append("&limit=100");

            if (!string.IsNullOrWhiteSpace(cursor))
            {
                query.Append("&after_cursor=").Append(Uri.EscapeDataString(cursor));
            }

            var page = await SendAsync<TPage>(HttpMethod.Get, query.ToString(), null, cancellationToken);
            results.AddRange(itemsSelector(page));
            cursor = nextCursorSelector(page);
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return results;
    }

    private async Task<string> GetOrCreateIdentityAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_identityId))
        {
            return _identityId!;
        }

        await _identityLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_identityId))
            {
                return _identityId!;
            }

            var identity = await SendAsync<IdentityResponse>(
                HttpMethod.Post,
                "/identities",
                new CreateIdentityRequest
                {
                    Entity = new Dictionary<string, object?>(),
                    IdentityRoles = new[] { "BUYER" },
                    Type = "PERSONAL"
                },
                cancellationToken);

            _identityId = identity.Id;
            _logger.LogInformation("Created Finix buyer identity {IdentityId}.", identity.Id);
            return identity.Id;
        }
        finally
        {
            _identityLock.Release();
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Finix-Version", FinixVersion);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}")));

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, responseBody);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return default!;
        }

        var result = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
        if (result is null)
        {
            throw new FinixApiException(
                (int)response.StatusCode,
                "INVALID_RESPONSE",
                "Finix returned an empty or invalid response.");
        }

        return result;
    }

    private FinixApiException BuildException(HttpStatusCode statusCode, string responseBody)
    {
        string? code = null;
        var messages = new List<string>();

        try
        {
            var error = JsonSerializer.Deserialize<ErrorResponse>(responseBody, JsonOptions);
            code = error?.Embedded?.Errors?.FirstOrDefault()?.Code;
            if (error?.Embedded?.Errors is not null)
            {
                foreach (var item in error.Embedded.Errors)
                {
                    if (!string.IsNullOrWhiteSpace(item.Message))
                    {
                        messages.Add(item.Message);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Finix occasionally returns non-JSON error bodies. Do not include the body in logs
            // because it could contain request data.
        }

        var message = messages.Count > 0
            ? string.Join(" ", messages)
            : $"Finix returned HTTP {(int)statusCode} ({statusCode}).";

        _logger.LogWarning("Finix request failed with HTTP {StatusCode} and code {Code}: {Message}",
            (int)statusCode,
            code ?? "UNKNOWN",
            message);

        return new FinixApiException((int)statusCode, code, message, messages);
    }

    private Uri BuildUri(string path)
    {
        var baseUrl = _baseUrl!.TrimEnd('/');
        var relative = path.StartsWith('/') ? path : "/" + path;
        return new Uri(baseUrl + relative);
    }

    private static string? TryResolveBaseUrl(FinixOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl!.Trim();
        }

        return options.Environment?.Trim().ToLowerInvariant() switch
        {
            "sandbox" => SandboxBaseUrl,
            "live" => LiveBaseUrl,
            _ => null
        };
    }

    private static string ResolveBaseUrl(FinixOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl!.Trim();
        }

        return options.Environment.Trim().ToLowerInvariant() switch
        {
            "sandbox" => SandboxBaseUrl,
            "live" => LiveBaseUrl,
            _ => throw new InvalidOperationException(
                $"Finix:Environment must be 'sandbox' or 'live', but was '{options.Environment}'.")
        };
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss");

    private static FinixPaymentInstrument MapPaymentInstrument(PaymentInstrumentResponse response)
    {
        return new FinixPaymentInstrument(
            response.Id,
            response.Enabled,
            response.Brand,
            response.LastFour,
            response.ExpirationMonth,
            response.ExpirationYear,
            response.CardType,
            response.Name,
            response.Identity);
    }

    private static FinixAuthorization MapAuthorization(AuthorizationResponse response)
    {
        return new FinixAuthorization(
            response.Id,
            response.State,
            response.Amount,
            response.IsVoid,
            response.VoidState,
            response.Transfer,
            response.ExpiresAt,
            response.FailureCode,
            response.FailureMessage,
            response.NetworkDetails?.Brand,
            response.Source);
    }

    private static FinixTransfer MapTransfer(TransferResponse response)
    {
        return new FinixTransfer(
            response.Id,
            response.Type,
            response.State,
            response.Amount,
            response.Fee ?? 0,
            response.Currency,
            response.CreatedAt,
            response.ParentTransfer,
            response.OperationKey,
            response.Source,
            response.FailureCode,
            response.FailureMessage);
    }

    private static FinixTransactionRecord MapAuthorizationTransaction(AuthorizationResponse response)
    {
        return new FinixTransactionRecord(
            "authorization",
            response.Id,
            "AUTHORIZATION",
            response.State,
            response.Amount,
            0,
            response.Currency,
            response.CreatedAt,
            null,
            response.Merchant,
            response.Source,
            response.FailureCode,
            response.FailureMessage,
            response.Tags ?? new Dictionary<string, string>());
    }

    private static FinixTransactionRecord MapTransferTransaction(TransferResponse response)
    {
        return new FinixTransactionRecord(
            "transfer",
            response.Id,
            response.Type,
            response.State,
            response.Amount,
            response.Fee ?? 0,
            response.Currency,
            response.CreatedAt,
            response.ParentTransfer,
            response.Merchant,
            response.Source,
            response.FailureCode,
            response.FailureMessage,
            response.Tags ?? new Dictionary<string, string>());
    }

    private static AddressRequest? ToFinixAddress(FinixAddress? address)
    {
        if (address is null
            || string.IsNullOrWhiteSpace(address.Line1)
            || string.IsNullOrWhiteSpace(address.City)
            || string.IsNullOrWhiteSpace(address.Region)
            || string.IsNullOrWhiteSpace(address.PostalCode)
            || string.IsNullOrWhiteSpace(address.Country))
        {
            return null;
        }

        return new AddressRequest
        {
            Line1 = address.Line1,
            Line2 = address.Line2,
            City = address.City,
            Region = address.Region,
            PostalCode = address.PostalCode,
            Country = address.Country
        };
    }

    private sealed class CreatePaymentInstrumentRequest
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "PAYMENT_CARD";
        [JsonPropertyName("number")] public string Number { get; set; } = string.Empty;
        [JsonPropertyName("expiration_month")] public int ExpirationMonth { get; set; }
        [JsonPropertyName("expiration_year")] public int ExpirationYear { get; set; }
        [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("identity")] public string Identity { get; set; } = string.Empty;
        [JsonPropertyName("address")] public AddressRequest? Address { get; set; }
    }

    private sealed class AddressRequest
    {
        [JsonPropertyName("line1")] public string Line1 { get; set; } = string.Empty;
        [JsonPropertyName("line2")] public string? Line2 { get; set; }
        [JsonPropertyName("city")] public string City { get; set; } = string.Empty;
        [JsonPropertyName("region")] public string Region { get; set; } = string.Empty;
        [JsonPropertyName("postal_code")] public string PostalCode { get; set; } = string.Empty;
        [JsonPropertyName("country")] public string Country { get; set; } = string.Empty;
    }

    private sealed class UpdatePaymentInstrumentRequest
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    }

    private sealed class CreateAuthorizationRequest
    {
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("currency")] public string Currency { get; set; } = string.Empty;
        [JsonPropertyName("merchant")] public string Merchant { get; set; } = string.Empty;
        [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
        [JsonPropertyName("idempotency_id")] public string IdempotencyId { get; set; } = string.Empty;
        [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
    }

    private sealed class CaptureAuthorizationRequest
    {
        [JsonPropertyName("capture_amount")] public long CaptureAmount { get; set; }
    }

    private sealed class VoidAuthorizationRequest
    {
        [JsonPropertyName("void_me")] public bool VoidMe { get; set; }
    }

    private sealed class CreateRefundRequest
    {
        [JsonPropertyName("refund_amount")] public long RefundAmount { get; set; }
    }

    private sealed class CreateIdentityRequest
    {
        [JsonPropertyName("entity")] public Dictionary<string, object?> Entity { get; set; } = new();
        [JsonPropertyName("identity_roles")] public string[] IdentityRoles { get; set; } = Array.Empty<string>();
        [JsonPropertyName("type")] public string Type { get; set; } = "PERSONAL";
    }

    private sealed class IdentityResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    }

    private sealed class PaymentInstrumentResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("brand")] public string? Brand { get; set; }
        [JsonPropertyName("last_four")] public string? LastFour { get; set; }
        [JsonPropertyName("expiration_month")] public int? ExpirationMonth { get; set; }
        [JsonPropertyName("expiration_year")] public int? ExpirationYear { get; set; }
        [JsonPropertyName("card_type")] public string? CardType { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("identity")] public string? Identity { get; set; }
    }

    private sealed class AuthorizationResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("is_void")] public bool IsVoid { get; set; }
        [JsonPropertyName("void_state")] public string? VoidState { get; set; }
        [JsonPropertyName("transfer")] public string? Transfer { get; set; }
        [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
        [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
        [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
        [JsonPropertyName("network_details")] public NetworkDetails? NetworkDetails { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("merchant")] public string? Merchant { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
        [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
    }

    private sealed class TransferResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("fee")] public long? Fee { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
        [JsonPropertyName("parent_transfer")] public string? ParentTransfer { get; set; }
        [JsonPropertyName("operation_key")] public string? OperationKey { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
        [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
        [JsonPropertyName("merchant")] public string? Merchant { get; set; }
        [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
    }

    private sealed class NetworkDetails
    {
        [JsonPropertyName("brand")] public string? Brand { get; set; }
        [JsonPropertyName("authorization_code")] public string? AuthorizationCode { get; set; }
    }

    private sealed class AuthorizationListResponse
    {
        [JsonPropertyName("_embedded")] public AuthorizationEmbedded? Embedded { get; set; }
        [JsonPropertyName("page")] public PageInfo? Page { get; set; }
    }

    private sealed class AuthorizationEmbedded
    {
        [JsonPropertyName("authorizations")] public List<AuthorizationResponse>? Authorizations { get; set; }
    }

    private sealed class TransferListResponse
    {
        [JsonPropertyName("_embedded")] public TransferEmbedded? Embedded { get; set; }
        [JsonPropertyName("page")] public PageInfo? Page { get; set; }
    }

    private sealed class TransferEmbedded
    {
        [JsonPropertyName("transfers")] public List<TransferResponse>? Transfers { get; set; }
    }

    private sealed class PageInfo
    {
        [JsonPropertyName("next_cursor")] public string? NextCursor { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("_embedded")] public ErrorEmbedded? Embedded { get; set; }
    }

    private sealed class ErrorEmbedded
    {
        [JsonPropertyName("errors")] public List<ErrorItem>? Errors { get; set; }
    }

    private sealed class ErrorItem
    {
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}

