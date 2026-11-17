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
using Microsoft.eShopWeb.ApplicationCore.Models.Payment;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

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
    private readonly string _baseUrl;
    private readonly string _basicAuthorization;

    public FinixClient(HttpClient httpClient, FinixOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _options.Validate();

        _baseUrl = ResolveBaseUrl(options).TrimEnd('/');
        _basicAuthorization = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
    }

    public async Task<FinixIdentity> CreateIdentityAsync(CardPaymentDetails card, CancellationToken cancellationToken)
    {
        var (firstName, lastName) = SplitName(card.Name);
        var body = new
        {
            type = "PERSONAL",
            identity_roles = new[] { "BUYER" },
            entity = new
            {
                first_name = firstName,
                last_name = lastName,
                personal_address = ToAddress(card.Address)
            }
        };

        return await SendAsync<FinixIdentity>(HttpMethod.Post, "/identities", body, cancellationToken);
    }

    public async Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(
        string identityId,
        CardPaymentDetails card,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            type = "PAYMENT_CARD",
            identity = identityId,
            name = card.Name,
            number = DigitsOnly(card.Number),
            expiration_month = card.ExpirationMonth,
            expiration_year = card.ExpirationYear,
            security_code = card.SecurityCode,
            address = ToAddress(card.Address)
        };

        return await SendAsync<FinixPaymentInstrument>(HttpMethod.Post, "/payment_instruments", body, cancellationToken);
    }

    public async Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken)
    {
        await SendAsync<FinixPaymentInstrument>(
            HttpMethod.Put,
            $"/payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            new { enabled = false },
            cancellationToken);
    }

    public async Task<FinixAuthorization> CreateAuthorizationAsync(
        string sourceId,
        long amountCents,
        string currency,
        string idempotencyId,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            amount = amountCents,
            currency,
            merchant = _options.MerchantId,
            source = sourceId,
            idempotency_id = idempotencyId,
            tags
        };

        try
        {
            return await SendAsync<FinixAuthorization>(HttpMethod.Post, "/authorizations", body, cancellationToken);
        }
        catch (FinixApiException ex) when (IsDuplicateIdempotency(ex))
        {
            var existing = await FindAuthorizationByIdempotencyIdAsync(idempotencyId, cancellationToken);
            if (existing != null)
            {
                return existing;
            }

            throw;
        }
    }

    public async Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        return await SendAsync<FinixAuthorization>(
            HttpMethod.Get,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            null,
            cancellationToken);
    }

    public async Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId,
        long captureAmountCents,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync<FinixAuthorization>(
                HttpMethod.Put,
                $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
                new { capture_amount = captureAmountCents },
                cancellationToken);
        }
        catch (FinixApiException ex) when (ex.Message.Contains("already captured", StringComparison.OrdinalIgnoreCase))
        {
            var latest = await GetAuthorizationAsync(authorizationId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(latest.Transfer))
            {
                return latest;
            }

            throw;
        }
    }

    public async Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync<FinixAuthorization>(
                HttpMethod.Put,
                $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
                new { void_me = true },
                cancellationToken);
        }
        catch (FinixApiException ex) when (ex.Message.Contains("void", StringComparison.OrdinalIgnoreCase))
        {
            var latest = await GetAuthorizationAsync(authorizationId, cancellationToken);
            if (latest.IsVoid || string.Equals(latest.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                return latest;
            }

            throw;
        }
    }

    public async Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        return await SendAsync<FinixTransfer>(
            HttpMethod.Get,
            $"/transfers/{Uri.EscapeDataString(transferId)}",
            null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<FinixFee>> ListFeesForTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        var fees = new List<FinixFee>();
        string? cursor = null;

        for (var page = 0; page < 1000; page++)
        {
            var path = $"/fees?linked_to={Uri.EscapeDataString(transferId)}&limit=100";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                path += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await SendAsync<FinixFeeListResponse>(HttpMethod.Get, path, null, cancellationToken);
            if (response.Embedded?.Fees != null)
            {
                fees.AddRange(response.Embedded.Fees);
            }

            cursor = response.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        return fees;
    }

    public async Task<FinixTransfer> CreateRefundAsync(
        string transferId,
        long refundAmountCents,
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync<FinixTransfer>(
                HttpMethod.Post,
                $"/transfers/{Uri.EscapeDataString(transferId)}/reversals",
                new { refund_amount = refundAmountCents, idempotency_id = idempotencyId },
                cancellationToken);
        }
        catch (FinixApiException ex) when (!string.IsNullOrWhiteSpace(ex.TransferId) && IsDuplicateIdempotency(ex))
        {
            return await GetTransferAsync(ex.TransferId!, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var transfers = new List<FinixTransfer>();
        string? cursor = null;
        var fromValue = Uri.EscapeDataString(from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss"));
        var toValue = Uri.EscapeDataString(to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss"));

        for (var page = 0; page < 10000; page++)
        {
            var path = $"/transfers?created_at.gte={fromValue}&created_at.lte={toValue}&limit=100";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                path += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await SendAsync<FinixTransferListResponse>(HttpMethod.Get, path, null, cancellationToken);
            if (response.Embedded?.Transfers != null)
            {
                transfers.AddRange(response.Embedded.Transfers);
            }

            cursor = response.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        return transfers;
    }

    private async Task<FinixAuthorization?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        string? cursor = null;

        for (var page = 0; page < 100; page++)
        {
            var path = "/authorizations?limit=100";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                path += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await SendAsync<FinixAuthorizationListResponse>(HttpMethod.Get, path, null, cancellationToken);
            var match = response.Embedded?.Authorizations?
                .FirstOrDefault(a => string.Equals(a.IdempotencyId, idempotencyId, StringComparison.Ordinal));
            if (match != null)
            {
                return match;
            }

            cursor = response.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        return null;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basicAuthorization);
        request.Headers.Add("Finix-Version", FinixVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new FinixApiException(
                (int)HttpStatusCode.BadGateway,
                new[]
                {
                    new FinixErrorDetail
                    {
                        Code = "NETWORK_ERROR",
                        Message = $"Unable to reach Finix: {ex.Message}"
                    }
                });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FinixApiException(
                (int)HttpStatusCode.GatewayTimeout,
                new[]
                {
                    new FinixErrorDetail
                    {
                        Code = "TIMEOUT",
                        Message = "The Finix API request timed out."
                    }
                });
        }

        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw BuildException((int)response.StatusCode, content);
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new FinixApiException(
                    (int)HttpStatusCode.BadGateway,
                    new[]
                    {
                        new FinixErrorDetail
                        {
                            Code = "EMPTY_RESPONSE",
                            Message = "Finix returned an empty response."
                        }
                    });
            }

            var result = JsonSerializer.Deserialize<T>(content, JsonOptions);
            if (result == null)
            {
                throw new FinixApiException(
                    (int)HttpStatusCode.BadGateway,
                    new[]
                    {
                        new FinixErrorDetail
                        {
                            Code = "INVALID_RESPONSE",
                            Message = "Finix returned a response that could not be deserialized."
                        }
                    });
            }

            return result;
        }
    }

    private static FinixApiException BuildException(int statusCode, string content)
    {
        var errors = new List<FinixErrorDetail>();
        try
        {
            var parsed = JsonSerializer.Deserialize<FinixErrorResponse>(content, JsonOptions);
            if (parsed?.Embedded?.Errors != null)
            {
                errors.AddRange(parsed.Embedded.Errors.Select(e => new FinixErrorDetail
                {
                    Code = e.Code,
                    Message = e.Message,
                    LogRef = e.LogRef,
                    Transfer = e.Transfer
                }));
            }
        }
        catch (JsonException)
        {
        }

        if (errors.Count == 0)
        {
            errors.Add(new FinixErrorDetail
            {
                Code = "FINIX_ERROR",
                Message = string.IsNullOrWhiteSpace(content)
                    ? $"Finix API request failed with status code {statusCode}."
                    : $"Finix API request failed with status code {statusCode}."
            });
        }

        return new FinixApiException(statusCode, errors);
    }

    private Uri BuildUri(string path)
    {
        var suffix = path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
        return new Uri(_baseUrl + suffix, UriKind.Absolute);
    }

    private static string ResolveBaseUrl(FinixOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl!;
        }

        return options.Environment.Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://finix.sandbox-payments-api.com",
            "live" => "https://finix.live-payments-api.com",
            _ => throw new InvalidOperationException("Finix:Environment must be 'sandbox' or 'live'.")
        };
    }

    private static object ToAddress(PaymentAddress? address)
    {
        address ??= new PaymentAddress();
        return new
        {
            line1 = Truncate(address.Line1, 35),
            line2 = string.IsNullOrWhiteSpace(address.Line2) ? null : Truncate(address.Line2, 35),
            city = Truncate(address.City, 20),
            region = Truncate(address.Region, 2),
            postal_code = Truncate(address.PostalCode, 7),
            country = NormalizeCountry(address.Country)
        };
    }

    private static string NormalizeCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return "USA";
        }

        var value = country.Trim().ToUpperInvariant();
        return value switch
        {
            "US" => "USA",
            "CA" => "CAN",
            "GB" => "GBR",
            _ => value
        };
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    private static string DigitsOnly(string value)
    {
        return new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
    }

    private static (string FirstName, string LastName) SplitName(string? name)
    {
        var parts = (name ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return ("eShop", "Shopper");
        }

        if (parts.Length == 1)
        {
            return (Truncate(parts[0], 20), "Shopper");
        }

        return (Truncate(parts[0], 20), Truncate(parts[^1], 20));
    }

    private static bool IsDuplicateIdempotency(FinixApiException exception)
    {
        return exception.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("idempotency", StringComparison.OrdinalIgnoreCase);
    }
}
