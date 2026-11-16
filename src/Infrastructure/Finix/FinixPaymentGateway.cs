using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public class FinixPaymentGateway : IFinixPaymentGateway
{
    private const string FinixVersion = "2022-02-01";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixPaymentGateway> _logger;

    public FinixPaymentGateway(
        HttpClient httpClient,
        FinixOptions options,
        ILogger<FinixPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<string> CreateBuyerIdentityAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<FinixIdentityRequest, FinixIdentityResponse>(
            HttpMethod.Post,
            "identities",
            new FinixIdentityRequest(),
            cancellationToken);

        if (response == null || string.IsNullOrWhiteSpace(response.Id))
        {
            throw new PaymentGatewayException(502, "Finix did not return a buyer identity id.");
        }

        return response.Id;
    }

    public async Task<FinixPaymentInstrumentResult> CreatePaymentInstrumentAsync(
        string identityId,
        FinixCard card,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixPaymentInstrumentRequest
        {
            Identity = identityId,
            Name = card.Name,
            Number = card.Number,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            SecurityCode = card.SecurityCode,
            Address = new FinixAddressRequest
            {
                Line1 = card.Address.Line1,
                Line2 = card.Address.Line2,
                City = card.Address.City,
                Region = card.Address.Region,
                PostalCode = card.Address.PostalCode,
                Country = card.Address.Country
            }
        };

        var response = await SendAsync<FinixPaymentInstrumentRequest, FinixPaymentInstrumentResponse>(
            HttpMethod.Post,
            "payment_instruments",
            request,
            cancellationToken);

        if (response == null || string.IsNullOrWhiteSpace(response.Id))
        {
            throw new PaymentGatewayException(502, "Finix did not return a payment instrument id.");
        }

        return new FinixPaymentInstrumentResult(
            response.Id,
            response.Brand,
            response.LastFour,
            response.ExpirationMonth,
            response.ExpirationYear,
            response.Identity);
    }

    public async Task DisablePaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<FinixDisablePaymentInstrumentRequest, FinixPaymentInstrumentResponse>(
            HttpMethod.Put,
            $"payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            new FinixDisablePaymentInstrumentRequest(),
            cancellationToken);
    }

    public async Task<FinixAuthorizationResult> CreateAuthorizationAsync(
        string sourcePaymentInstrumentId,
        long amountCents,
        string currency,
        string idempotencyId,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixAuthorizationRequest
        {
            Amount = amountCents,
            Currency = currency,
            Merchant = _options.MerchantId,
            Source = sourcePaymentInstrumentId,
            IdempotencyId = idempotencyId,
            Tags = tags
        };

        try
        {
            var response = await SendAsync<FinixAuthorizationRequest, FinixAuthorizationResponse>(
                HttpMethod.Post,
                "authorizations",
                request,
                cancellationToken);

            return MapAuthorization(response);
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode == 422 &&
                                                 ex.Message.Contains("Duplicate authorization", StringComparison.OrdinalIgnoreCase))
        {
            var existing = await FindAuthorizationByIdempotencyIdAsync(idempotencyId, cancellationToken);
            if (existing != null &&
                existing.Amount == amountCents &&
                string.Equals(existing.Source, sourcePaymentInstrumentId, StringComparison.Ordinal))
            {
                return existing;
            }

            throw;
        }
    }

    public async Task<FinixAuthorizationResult> CaptureAuthorizationAsync(
        string authorizationId,
        long captureAmountCents,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<FinixCaptureAuthorizationRequest, FinixAuthorizationResponse>(
            HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            new FinixCaptureAuthorizationRequest { CaptureAmount = captureAmountCents },
            cancellationToken);

        return MapAuthorization(response);
    }

    public async Task<FinixAuthorizationResult> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<FinixVoidAuthorizationRequest, FinixAuthorizationResponse>(
            HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            new FinixVoidAuthorizationRequest { VoidMe = true },
            cancellationToken);

        return MapAuthorization(response);
    }

    public async Task<FinixAuthorizationResult?> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await SendAsync<FinixAuthorizationResponse>(
                HttpMethod.Get,
                $"authorizations/{Uri.EscapeDataString(authorizationId)}",
                cancellationToken);
            return response == null ? null : MapAuthorization(response);
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public async Task<FinixTransferResult?> GetTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await SendAsync<FinixTransferResponse>(
                HttpMethod.Get,
                $"transfers/{Uri.EscapeDataString(transferId)}",
                cancellationToken);
            return response == null ? null : MapTransfer(response);
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public async Task<decimal?> GetTransferFeeCentsAsync(
        string transferId,
        CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        var total = 0m;
        var count = 0;

        do
        {
            var path = $"fees?linked_to={Uri.EscapeDataString(transferId)}&limit=100";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                path += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await SendAsync<FinixFeeListResponse>(HttpMethod.Get, path, cancellationToken);
            var fees = response?.Embedded?.Fees ?? new List<FinixFeeResponse>();
            count += fees.Count;
            total += fees.Sum(f => f.Amount);
            cursor = response?.Page?.NextCursor;
        } while (!string.IsNullOrWhiteSpace(cursor));

        return count == 0 ? null : total;
    }

    public async Task<FinixTransferResult> RefundTransferAsync(
        string transferId,
        long refundAmountCents,
        string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await SendAsync<FinixRefundRequest, FinixTransferResponse>(
                HttpMethod.Post,
                $"transfers/{Uri.EscapeDataString(transferId)}/reversals",
                new FinixRefundRequest { RefundAmount = refundAmountCents, IdempotencyId = idempotencyId },
                cancellationToken);

            if (response == null || string.IsNullOrWhiteSpace(response.Id))
            {
                throw new PaymentGatewayException(502, "Finix did not return a refund transfer id.");
            }

            return MapTransfer(response);
        }
        catch (PaymentGatewayException ex) when (
            ex.Message.Contains("Duplicate Reversal", StringComparison.OrdinalIgnoreCase))
        {
            var existing = await FindReversalByIdempotencyIdAsync(idempotencyId, transferId, cancellationToken);
            if (existing != null && existing.Amount == refundAmountCents)
            {
                return existing;
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<FinixAuthorizationResult>> ListAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FinixAuthorizationResult>();
        string? cursor = null;

        do
        {
            var path = BuildListPath("authorizations", from, to, cursor);
            var response = await SendAsync<FinixAuthorizationListResponse>(HttpMethod.Get, path, cancellationToken);
            var authorizations = response?.Embedded?.Authorizations ?? new List<FinixAuthorizationResponse>();
            results.AddRange(authorizations.Select(MapAuthorization));
            cursor = response?.Page?.NextCursor;
        } while (!string.IsNullOrWhiteSpace(cursor));

        return results;
    }

    public async Task<IReadOnlyList<FinixTransferResult>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FinixTransferResult>();
        string? cursor = null;

        do
        {
            var path = BuildListPath("transfers", from, to, cursor);
            var response = await SendAsync<FinixTransferListResponse>(HttpMethod.Get, path, cancellationToken);
            var transfers = response?.Embedded?.Transfers ?? new List<FinixTransferResponse>();
            results.AddRange(transfers.Select(MapTransfer));
            cursor = response?.Page?.NextCursor;
        } while (!string.IsNullOrWhiteSpace(cursor));

        return results;
    }

    private async Task<FinixAuthorizationResult?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        var path = $"authorizations?idempotency_id={Uri.EscapeDataString(idempotencyId)}&limit=100";
        var response = await SendAsync<FinixAuthorizationListResponse>(HttpMethod.Get, path, cancellationToken);
        var authorization = response?.Embedded?.Authorizations?.FirstOrDefault();
        return authorization == null ? null : MapAuthorization(authorization);
    }

    private async Task<FinixTransferResult?> FindReversalByIdempotencyIdAsync(
        string idempotencyId,
        string parentTransferId,
        CancellationToken cancellationToken)
    {
        var path = $"transfers?idempotency_id={Uri.EscapeDataString(idempotencyId)}" +
                   $"&parent_transfer_id={Uri.EscapeDataString(parentTransferId)}&limit=100";
        var response = await SendAsync<FinixTransferListResponse>(HttpMethod.Get, path, cancellationToken);
        var transfer = response?.Embedded?.Transfers?.FirstOrDefault(t =>
            string.Equals(t.Type, "REVERSAL", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.ParentTransfer, parentTransferId, StringComparison.Ordinal));
        return transfer == null ? null : MapTransfer(transfer);
    }

    private static string BuildListPath(string resource, DateTimeOffset from, DateTimeOffset to, string? cursor)
    {
        var builder = new StringBuilder(resource);
        builder.Append("?created_at.gte=");
        builder.Append(Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        builder.Append("&created_at.lte=");
        builder.Append(Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        builder.Append("&limit=100");

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            builder.Append("&after_cursor=");
            builder.Append(Uri.EscapeDataString(cursor));
        }

        return builder.ToString();
    }

    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        return await SendCoreAsync<TResponse>(request, cancellationToken);
    }

    private async Task<TResponse?> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        return await SendCoreAsync<TResponse>(request, cancellationToken);
    }

    private async Task<TResponse?> SendCoreAsync<TResponse>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _options.Validate();

        if (request.RequestUri != null && !request.RequestUri.IsAbsoluteUri)
        {
            request.RequestUri = new Uri(
                _options.ResolveBaseUrl().TrimEnd('/') + "/" + request.RequestUri.OriginalString.TrimStart('/'));
        }

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}")));
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Remove("Finix-Version");
        request.Headers.Add("Finix-Version", FinixVersion);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryDeserialize<FinixErrorResponse>(content);
            var first = error?.Embedded?.Errors?.FirstOrDefault();
            var message = first?.Message ?? $"Finix request failed with HTTP {(int)response.StatusCode}.";

            _logger.LogWarning(
                "Finix request {Method} {Path} failed with {StatusCode} ({Code})",
                request.Method,
                request.RequestUri?.PathAndQuery,
                (int)response.StatusCode,
                first?.Code);

            throw new PaymentGatewayException(
                (int)response.StatusCode,
                message,
                first?.Code,
                first?.LogRef,
                first?.Authorization);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        return TryDeserialize<TResponse>(content);
    }

    private static T? TryDeserialize<T>(string content)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static FinixAuthorizationResult MapAuthorization(FinixAuthorizationResponse? response)
    {
        if (response == null || string.IsNullOrWhiteSpace(response.Id))
        {
            throw new PaymentGatewayException(502, "Finix did not return an authorization id.");
        }

        return new FinixAuthorizationResult(
            response.Id,
            response.State,
            response.Amount,
            response.Currency,
            response.Source,
            response.Transfer,
            response.ExpiresAt,
            response.IsVoid,
            response.VoidState,
            response.FailureCode,
            response.FailureMessage,
            response.Tags,
            response.CreatedAt);
    }

    private static FinixTransferResult MapTransfer(FinixTransferResponse? response)
    {
        if (response == null || string.IsNullOrWhiteSpace(response.Id))
        {
            throw new PaymentGatewayException(502, "Finix did not return a transfer id.");
        }

        return new FinixTransferResult(
            response.Id,
            response.State,
            response.Amount,
            response.Currency,
            response.Type,
            response.OperationKey,
            response.ParentTransfer,
            response.CreatedAt,
            response.UpdatedAt,
            response.Tags,
            response.FailureCode,
            response.FailureMessage);
    }
}
