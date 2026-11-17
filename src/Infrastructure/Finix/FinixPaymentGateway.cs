using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Errors;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Models.Enums;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.FeesApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using FinixApiReference.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FinixAddress = FinixApiReference.Models.Address;
using FinixBuyer = FinixApiReference.Models.Buyer;
using FinixEntity = FinixApiReference.Models.Entity5;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public sealed class FinixPaymentGateway : IFinixPaymentGateway
{
    private const string FinixVersion = "2022-02-01";
    private readonly FinixApiReferenceClient _client;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixPaymentGateway> _logger;

    public FinixPaymentGateway(IOptions<FinixOptions> options, ILogger<FinixPaymentGateway> logger)
    {
        _options = options.Value;
        _logger = logger;

        var clientOptions = new FinixApiReferenceClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = _options.Username,
                Password = _options.Password
            },
            Server = new ServerOptions
            {
                Default = new DefaultOptions
                {
                    Production = new DefaultOptions.ProductionOptions
                    {
                        BaseUrl = _options.ResolveBaseUrl()
                    }
                }
            },
            Logging = new LoggingOptions
            {
                LogRequestHeaders = false,
                LogResponseHeaders = false,
                LogRequestBody = false,
                RedactedKeys = new[]
                {
                    "password", "number", "security_code", "cvv", "cvc", "access_token", "api_key"
                }
            }
        };

        _client = new FinixApiReferenceClient(new HttpClient { Timeout = TimeSpan.FromSeconds(100) }, clientOptions);
    }

    public async Task<FinixPaymentInstrumentInfo> CreateCardPaymentInstrumentAsync(
        string? buyerEmail,
        CardDetails card,
        CancellationToken cancellationToken = default)
    {
        var (firstName, lastName) = SplitName(card.Name);
        var identity = await _client.Identities.CreateIdentity(new CreateIdentityRequest
        {
            Body = IdentitiesRequest.Buyer(new FinixBuyer
            {
                Entity = new FinixEntity
                {
                    Email = buyerEmail,
                    FirstName = firstName,
                    LastName = lastName,
                    PersonalAddress = ToFinixAddress(card.BillingAddress)
                },
                IdentityRoles = new[] { IdentityRole5.Buyer },
                Type = Type10.Personal
            })
        }, cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(identity.Id))
        {
            throw new InvalidOperationException("Finix did not return an Identity id for the shopper.");
        }

        var response = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(new CreatePaymentInstrumentRequest
        {
            Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(new PaymentInstrumentCard1
            {
                Address = ToFinixAddress(card.BillingAddress),
                ExpirationMonth = card.ExpirationMonth,
                ExpirationYear = card.ExpirationYear,
                Identity = identity.Id!,
                Name = card.Name,
                Number = DigitsOnly(card.Number),
                SecurityCode = card.SecurityCode
            })
        }, cancellationToken: cancellationToken);

        if (!response.TryGetPaymentInstrumentCard(out var instrument) || string.IsNullOrWhiteSpace(instrument?.Id))
        {
            throw new InvalidOperationException("Finix did not return a card Payment Instrument.");
        }

        return MapPaymentInstrument(instrument!);
    }

    public async Task<FinixPaymentInstrumentInfo?> GetPaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.PaymentInstrumentsApi.GetPaymentInstrument(new GetPaymentInstrumentRequest
        {
            PaymentInstrumentId = paymentInstrumentId
        }, cancellationToken: cancellationToken);

        return response.TryGetPaymentInstrumentCard(out var instrument) && instrument is not null
            ? MapPaymentInstrument(instrument)
            : null;
    }

    public async Task DisablePaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken = default)
    {
        await _client.PaymentInstrumentsApi.UpdatePaymentInstrument(new UpdatePaymentInstrumentRequest
        {
            PaymentInstrumentId = paymentInstrumentId,
            Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                new PaymentInstrumentDisableAPaymentInstrument { Enabled = false })
        }, cancellationToken: cancellationToken);
    }

    public async Task<FinixAuthorizationInfo> CreateAuthorizationAsync(
        long amountCents,
        string currency,
        string sourcePaymentInstrumentId,
        string idempotencyId,
        string orderId,
        string operationId,
        string? securityCode,
        CancellationToken cancellationToken = default)
    {
        Authorization authorization;
        try
        {
            authorization = await _client.AuthorizationsApi.CreateAuthorization(new CreateAuthorizationRequest
            {
                Body = new AuthorizationAllFields
                {
                    Amount = amountCents,
                    Currency = ToCurrency(currency),
                    Merchant = _options.MerchantId,
                    Source = sourcePaymentInstrumentId,
                    SecurityCode = securityCode,
                    IdempotencyId = idempotencyId,
                    Tags = new Dictionary<string, string>
                    {
                        ["orderId"] = orderId,
                        ["eshopOperationId"] = operationId
                    }
                }
            }, cancellationToken: cancellationToken);
        }
        catch (ApiException<CreateAuthorizationError> ex)
        {
            var detail = DescribeAuthorizationError(ex);
            if (detail.Contains("Duplicate authorization", StringComparison.OrdinalIgnoreCase))
            {
                var existing = await FindAuthorizationByIdempotencyIdAsync(idempotencyId, cancellationToken);
                if (existing is not null)
                {
                    return existing;
                }
            }

            throw new PaymentException(402, $"Finix authorization failed: {detail}");
        }

        return MapAuthorization(authorization);
    }

    public async Task<FinixAuthorizationInfo> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _client.AuthorizationsApi.GetAuthorization(new GetAuthorizationRequest
        {
            AuthorizationId = authorizationId
        }, cancellationToken: cancellationToken);

        return MapAuthorization(authorization);
    }

    public async Task<FinixAuthorizationInfo> CaptureAuthorizationAsync(
        string authorizationId,
        long amountCents,
        string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        var body = new CaptureAuthorization
        {
            CaptureAmount = amountCents
        };
        body.AdditionalProperties.Set("idempotency_id", idempotencyId);

        Authorization authorization;
        try
        {
            authorization = await _client.AuthorizationsApi.CaptureAuthorization(new CaptureAuthorizationRequest
            {
                AuthorizationId = authorizationId,
                Body = body
            }, cancellationToken: cancellationToken);
        }
        catch (ApiException<CaptureAuthorizationError> ex)
        {
            var detail = DescribeCaptureError(ex);
            var current = await GetAuthorizationAsync(authorizationId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(current.TransferId))
            {
                return current;
            }

            throw new PaymentException(409, $"Finix capture failed: {detail}");
        }

        return MapAuthorization(authorization);
    }

    public async Task<FinixAuthorizationInfo> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _client.AuthorizationsApi.VoidAuthorization(new VoidAuthorizationRequest
        {
            AuthorizationIdVoidTo = authorizationId,
            Body = new VoidAnAuthorization { VoidMe = true }
        }, cancellationToken: cancellationToken);

        return MapAuthorization(authorization);
    }

    public async Task<FinixTransferInfo> GetTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default)
    {
        var transfer = await _client.TransfersApi.GetTransfer(new GetTransferRequest
        {
            TransferId = transferId
        }, cancellationToken: cancellationToken);

        return MapTransfer(transfer);
    }

    public async Task<decimal> GetFeeCentsAsync(
        string transferId,
        CancellationToken cancellationToken = default)
    {
        decimal total = 0;
        string? cursor = null;
        do
        {
            var response = await _client.FeesApi.ListFees(new ListFeesRequest
            {
                LinkedTo = transferId,
                Limit = 100,
                AfterCursor = cursor
            }, cancellationToken: cancellationToken);

            foreach (var fee in response.Embedded?.Fees ?? Array.Empty<Fee>())
            {
                total += (decimal)(fee.Amount ?? 0);
            }

            cursor = response.Page?.NextCursor;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return total;
    }

    public async Task<FinixRefundInfo> RefundTransferAsync(
        string transferId,
        long amountCents,
        string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        var body = new RefundTransfer
        {
            RefundAmount = checked((int)amountCents)
        };
        body.AdditionalProperties.Set("idempotency_id", idempotencyId);

        Transfer transfer;
        try
        {
            transfer = await _client.TransfersApi.CreateTransferReversal(new CreateTransferReversalRequest
            {
                TransferId = transferId,
                Body = body
            }, cancellationToken: cancellationToken);
        }
        catch (ApiException<CreateTransferReversalError> ex)
        {
            var detail = DescribeReversalError(ex);
            if (detail.Contains("Duplicate Reversal", StringComparison.OrdinalIgnoreCase))
            {
                var reversals = await _client.TransfersApi.ListTransferReversals(new ListTransferReversalsRequest
                {
                    TransferId = transferId,
                    Limit = 100
                }, cancellationToken: cancellationToken);

                var existing = reversals.Embedded?.Transfers?
                    .FirstOrDefault(t => string.Equals(t.IdempotencyId, idempotencyId, StringComparison.Ordinal));
                if (existing?.Id is not null)
                {
                    return new FinixRefundInfo(
                        existing.Id,
                        existing.State?.Value,
                        existing.Amount ?? amountCents,
                        existing.FailureCode,
                        existing.FailureMessage);
                }
            }

            throw new PaymentException(402, $"Finix refund failed: {detail}");
        }

        if (string.IsNullOrWhiteSpace(transfer.Id))
        {
            throw new InvalidOperationException("Finix did not return a refund Transfer id.");
        }

        return new FinixRefundInfo(
            transfer.Id!,
            transfer.State?.Value,
            transfer.Amount ?? amountCents,
            transfer.FailureCode,
            transfer.FailureMessage);
    }

    public async Task<IReadOnlyList<FinixTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var records = new List<FinixTransactionRecord>();

        string? transferCursor = null;
        string? previousTransferCursor = null;
        do
        {
            // The SDK serializes created_at filters with fractional seconds and a trailing Z,
            // which Finix rejects for these list endpoints. List all pages through the SDK and
            // apply the caller's date range to Finix's own created_at values below.
            var response = await _client.TransfersApi.ListTransfers(new ListTransfersRequest
            {
                Limit = 100,
                AfterCursor = transferCursor
            }, cancellationToken: cancellationToken);

            foreach (var transfer in response.Embedded?.Transfers ?? Array.Empty<Transfer>())
            {
                if (string.IsNullOrWhiteSpace(transfer.Id))
                {
                    continue;
                }

                records.Add(new FinixTransactionRecord(
                    transfer.Id!,
                    "TRANSFER",
                    transfer.State?.Value,
                    transfer.Amount,
                    transfer.Type?.Value,
                    transfer.ParentTransfer,
                    transfer.CreatedAt,
                    ReadTags(transfer.Tags),
                    transfer.FailureCode,
                    transfer.FailureMessage));
            }

            previousTransferCursor = transferCursor;
            transferCursor = response.Page?.NextCursor;
        }
        while (!string.IsNullOrWhiteSpace(transferCursor) && transferCursor != previousTransferCursor);

        string? authorizationCursor = null;
        string? previousAuthorizationCursor = null;
        do
        {
            var response = await _client.AuthorizationsApi.ListAuthorizations(new ListAuthorizationsRequest
            {
                Limit = 100,
                AfterCursor = authorizationCursor
            }, cancellationToken: cancellationToken);

            foreach (var authorization in response.Embedded?.Authorizations ?? Array.Empty<Authorization>())
            {
                if (string.IsNullOrWhiteSpace(authorization.Id))
                {
                    continue;
                }

                records.Add(new FinixTransactionRecord(
                    authorization.Id!,
                    "AUTHORIZATION",
                    authorization.State?.Value,
                    authorization.Amount,
                    "AUTHORIZATION",
                    null,
                    authorization.CreatedAt,
                    ReadTags(authorization.Tags),
                    authorization.FailureCode,
                    authorization.FailureMessage));
            }

            previousAuthorizationCursor = authorizationCursor;
            authorizationCursor = response.Page?.NextCursor;
        }
        while (!string.IsNullOrWhiteSpace(authorizationCursor) && authorizationCursor != previousAuthorizationCursor);

        var inRange = records
            .Where(r => r.CreatedAt.HasValue && r.CreatedAt.Value >= from && r.CreatedAt.Value <= to)
            .ToList();

        _logger.LogInformation(
            "Retrieved {Count} Finix transactions between {From} and {To}",
            inRange.Count,
            from,
            to);

        return inRange;
    }

    private async Task<FinixAuthorizationInfo?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        var response = await _client.AuthorizationsApi.ListAuthorizations(new ListAuthorizationsRequest
        {
            IdempotencyId = idempotencyId,
            Limit = 10
        }, cancellationToken: cancellationToken);

        var authorization = response.Embedded?.Authorizations?
            .FirstOrDefault(a => string.Equals(a.IdempotencyId, idempotencyId, StringComparison.Ordinal));

        return authorization is null ? null : MapAuthorization(authorization);
    }

    private static string DescribeReversalError(ApiException<CreateTransferReversalError> ex)
    {
        if (ex.Error.TryGetGenericError(out var generic) && generic.Embedded?.Errors is { } errors)
        {
            return string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return raw.ReadAsString();
        }

        return ex.Message;
    }

    private static string DescribeCaptureError(ApiException<CaptureAuthorizationError> ex)
    {
        var details = new List<string>();
        if (ex.Error.TryGetGenericError(out var generic) && generic.Embedded?.Errors is { } errors)
        {
            details.AddRange(errors.Select(e => $"{e.Code}: {e.Message}"));
        }

        return details.Count > 0 ? string.Join("; ", details) : ex.Message;
    }

    private static string DescribeAuthorizationError(ApiException<CreateAuthorizationError> ex)
    {
        var details = new List<string>();
        if (ex.Error.TryGetGenericError(out var generic) && generic.Embedded?.Errors is { } errors)
        {
            details.AddRange(errors.Select(e => $"{e.Code}: {e.Message}"));
        }

        return details.Count > 0 ? string.Join("; ", details) : ex.Message;
    }

    private static FinixAuthorizationInfo MapAuthorization(Authorization authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization.Id))
        {
            throw new InvalidOperationException("Finix did not return an Authorization id.");
        }

        return new FinixAuthorizationInfo(
            authorization.Id!,
            authorization.State?.Value,
            authorization.Amount ?? 0,
            authorization.Source,
            authorization.Transfer,
            authorization.IsVoid ?? false,
            authorization.ExpiresAt,
            authorization.FailureCode,
            authorization.FailureMessage);
    }

    private static FinixTransferInfo MapTransfer(Transfer transfer)
    {
        if (string.IsNullOrWhiteSpace(transfer.Id))
        {
            throw new InvalidOperationException("Finix did not return a Transfer id.");
        }

        return new FinixTransferInfo(
            transfer.Id!,
            transfer.State?.Value,
            transfer.Amount,
            transfer.Type?.Value,
            transfer.ParentTransfer,
            transfer.CreatedAt,
            ReadTags(transfer.Tags),
            transfer.FailureCode,
            transfer.FailureMessage);
    }

    private static FinixPaymentInstrumentInfo MapPaymentInstrument(PaymentInstrumentCard instrument)
    {
        return new FinixPaymentInstrumentInfo(
            instrument.Id!,
            instrument.Identity,
            instrument.Brand?.Value,
            instrument.LastFour,
            instrument.ExpirationMonth,
            instrument.ExpirationYear,
            instrument.CardType?.Value);
    }

    private static FinixAddress? ToFinixAddress(BillingAddressDetails? address)
    {
        if (address is null ||
            string.IsNullOrWhiteSpace(address.Street) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.State) ||
            string.IsNullOrWhiteSpace(address.ZipCode))
        {
            return null;
        }

        return new FinixAddress
        {
            Line1 = address.Street,
            City = address.City,
            Region = address.State,
            PostalCode = address.ZipCode,
            Country = ToCountry(address.Country)
        };
    }

    private static Country ToCountry(string? country)
    {
        return country?.Trim().ToUpperInvariant() switch
        {
            "CAN" or "CA" or "CANADA" => Country.Can,
            "GBR" or "GB" or "UK" or "UNITED KINGDOM" => Country.Gbr,
            _ => Country.Usa
        };
    }

    private static Currency ToCurrency(string currency)
    {
        return currency?.Trim().ToUpperInvariant() switch
        {
            "CAD" => Currency.Cad,
            "USD" => Currency.Usd,
            _ => throw new InvalidOperationException(
                $"Finix Currency '{currency}' is not supported by this integration. Use USD or CAD.")
        };
    }

    private static (string? FirstName, string? LastName) SplitName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, null);
        }

        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return (Truncate(parts[0], 20), null);
        }

        return (Truncate(parts[0], 20), Truncate(parts[^1], 20));
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string DigitsOnly(string value) =>
        new(value.Where(char.IsDigit).ToArray());

    private static IReadOnlyDictionary<string, string> ReadTags(object? tags)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tags is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.ToString();
            }
        }
        else if (tags is IDictionary<string, string> dictionary)
        {
            foreach (var pair in dictionary)
            {
                result[pair.Key] = pair.Value;
            }
        }

        return result;
    }
}
