using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using FinixApiReference.Core.ErrorResponse;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Core.Models;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Models.Enums;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using FinixApiReference.Servers;
using FinixAuthorization = FinixApiReference.Models.Authorization;
using FinixIdentity = FinixApiReference.Models.Identity;
using FinixTransfer = FinixApiReference.Models.Transfer;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public sealed class FinixCardDetails
{
    public string Number { get; init; } = string.Empty;
    public int ExpirationMonth { get; init; }
    public int ExpirationYear { get; init; }
    public string? SecurityCode { get; init; }
    public string Name { get; init; } = string.Empty;
    public FinixBillingAddress? BillingAddress { get; init; }
}

public sealed class FinixBillingAddress
{
    public string? Line1 { get; init; }
    public string? City { get; init; }
    public string? Region { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
}

public sealed class FinixPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly FinixApiReferenceClient _client;
    private readonly FinixOptions _options;

    public FinixPaymentGateway(FinixApiReferenceClient client, IOptions<FinixOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public string CurrencyCode => _options.Currency.Trim().ToUpperInvariant();
    public string MerchantId => _options.MerchantId;

    public static FinixApiReferenceClient CreateClient(FinixOptions options, HttpClient httpClient)
    {
        options.Validate();

        var clientOptions = new FinixApiReferenceClientOptions
        {
            Environment = ServerEnvironment.Production,
            BasicAuth = new BasicAuthCredentials
            {
                Username = options.Username,
                Password = options.Password
            },
            Server = new ServerOptions
            {
                Default = new DefaultOptions
                {
                    Production = new DefaultOptions.ProductionOptions
                    {
                        BaseUrl = options.ResolveBaseUrl()
                    }
                }
            },
            Logging = new LoggingOptions
            {
                LoggerFactory = NullLoggerFactory.Instance
            }
        };

        return new FinixApiReferenceClient(httpClient, clientOptions);
    }

    public async Task<FinixIdentity> CreateBuyerIdentityAsync(string email, CancellationToken cancellationToken)
    {
        try
        {
            var request = new CreateIdentityRequest
            {
                Body = IdentitiesRequest.Buyer(new Buyer
                {
                    Entity = new Entity5
                    {
                        Email = email
                    },
                    IdentityRoles = new[] { IdentityRole5.Buyer },
                    Type = Type10.Personal
                })
            };

            var identity = await _client.Identities.CreateIdentity(request, null, cancellationToken);
            if (string.IsNullOrWhiteSpace(identity.Id))
            {
                throw new FinixGatewayException("Finix did not return an identity id.", HttpStatusCode.BadGateway);
            }

            return identity;
        }
        catch (Exception exception) when (exception is ApiException or SdkException)
        {
            throw Wrap(exception, "Creating the Finix buyer identity");
        }
    }

    public async Task<PaymentInstrumentCard> CreateCardPaymentInstrumentAsync(
        string identityId,
        FinixCardDetails card,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = new PaymentInstrumentCard1
            {
                Identity = identityId,
                Name = card.Name,
                Number = NormalizeCardNumber(card.Number),
                ExpirationMonth = card.ExpirationMonth,
                ExpirationYear = card.ExpirationYear,
                SecurityCode = card.SecurityCode,
                Address = MapAddress(card.BillingAddress)
            };

            var request = new CreatePaymentInstrumentRequest
            {
                Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(body)
            };

            var result = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(request, null, cancellationToken);
            if (!result.TryGetPaymentInstrumentCard(out var paymentInstrument) ||
                string.IsNullOrWhiteSpace(paymentInstrument.Id))
            {
                throw new FinixGatewayException("Finix did not return a card payment instrument.", HttpStatusCode.BadGateway);
            }

            return paymentInstrument;
        }
        catch (Exception exception) when (exception is ApiException or SdkException)
        {
            throw Wrap(exception, "Saving the card with Finix");
        }
    }

    public async Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken)
    {
        try
        {
            var request = new UpdatePaymentInstrumentRequest
            {
                PaymentInstrumentId = paymentInstrumentId,
                Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                    new PaymentInstrumentDisableAPaymentInstrument { Enabled = false })
            };

            await _client.PaymentInstrumentsApi.UpdatePaymentInstrument(request, null, cancellationToken);
        }
        catch (Exception exception) when (exception is ApiException or SdkException)
        {
            throw Wrap(exception, "Removing the card from Finix");
        }
    }

    public async Task<FinixAuthorization> CreateAuthorizationAsync(
        string source,
        long amountCents,
        string idempotencyId,
        string? securityCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new CreateAuthorizationRequest
            {
                Body = AuthorizationsRequest.AuthorizationAllFields(new AuthorizationAllFields
                {
                    Amount = amountCents,
                    Currency = ToCurrency(CurrencyCode),
                    Merchant = MerchantId,
                    Source = source,
                    IdempotencyId = idempotencyId,
                    SecurityCode = securityCode,
                    PartialAuthorization = false,
                    StatementDescriptor = "ESHOPONWEB"
                })
            };

            return await _client.AuthorizationsApi.CreateAuthorization(request, null, cancellationToken);
        }
        catch (Exception exception) when (exception is ApiException or SdkException)
        {
            throw Wrap(exception, "Authorizing the card with Finix");
        }
    }

    public async Task<FinixAuthorization?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.AuthorizationsApi.ListAuthorizations(
                new ListAuthorizationsRequest
                {
                    Merchant = MerchantId,
                    IdempotencyId = idempotencyId,
                    Limit = 10
                },
                null,
                cancellationToken);

            return response.Embedded?.Authorizations?.FirstOrDefault();
        }
        catch (Exception exception) when (exception is ApiException or SdkException)
        {
            throw Wrap(exception, "Looking up the Finix authorization");
        }
    }

    public async Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId },
                null,
                cancellationToken);
        }
        catch (Exception exception) when (exception is ApiException or SdkException)
        {
            throw Wrap(exception, "Fetching the Finix authorization");
        }
    }

    public async Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId,
        long amountCents,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new CaptureAuthorizationRequest
            {
                AuthorizationId = authorizationId,
                Body = AuthorizationsRequest1.CaptureAuthorization(new CaptureAuthorization
                {
                    CaptureAmount = amountCents
                })
            };

            return await _client.AuthorizationsApi.CaptureAuthorization(request, null, cancellationToken);
        }
        catch (Exception exception) when (exception is ApiException or SdkException)
        {
            throw Wrap(exception, "Capturing the Finix authorization");
        }
    }

    public async Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            var request = new VoidAuthorizationRequest
            {
                AuthorizationIdVoidTo = authorizationId,
                Body = new VoidAnAuthorization { VoidMe = true }
            };

            return await _client.AuthorizationsApi.VoidAuthorization(request, null, cancellationToken);
        }
        catch (Exception exception) when (exception is ApiException or SdkException)
        {
            throw Wrap(exception, "Releasing the Finix authorization");
        }
    }

    public async Task<FinixTransfer> CreateRefundAsync(
        string parentTransferId,
        long amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var refund = new RefundTransfer
            {
                RefundAmount = checked((int)amountCents)
            };
            refund.AdditionalProperties.Set("idempotency_id", idempotencyKey);

            var request = new CreateTransferReversalRequest
            {
                TransferId = parentTransferId,
                Body = TransfersReversalsRequest.RefundTransfer(refund)
            };

            return await _client.TransfersApi.CreateTransferReversal(request, null, cancellationToken);
        }
        catch (Exception exception) when (exception is ApiException or SdkException)
        {
            throw Wrap(exception, "Refunding the Finix transfer");
        }
    }

    public async Task<FinixTransfer?> FindRefundByIdempotencyAsync(
        string parentTransferId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            string? cursor = null;
            do
            {
                var response = await _client.TransfersApi.ListTransferReversals(
                    new ListTransferReversalsRequest
                    {
                        TransferId = parentTransferId,
                        AfterCursor = cursor,
                        Limit = 100
                    },
                    null,
                    cancellationToken);

                var match = response.Embedded?.Transfers?
                    .FirstOrDefault(t => string.Equals(t.IdempotencyId, idempotencyKey, StringComparison.Ordinal));
                if (match is not null)
                {
                    return match;
                }

                cursor = response.Page?.NextCursor;
            } while (!string.IsNullOrWhiteSpace(cursor));

            return null;
        }
        catch (Exception exception) when (exception is ApiException or SdkException)
        {
            throw Wrap(exception, "Looking up the Finix refund");
        }
    }

    public async Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = transferId },
                null,
                cancellationToken);
        }
        catch (Exception exception) when (exception is ApiException or SdkException)
        {
            throw Wrap(exception, "Fetching the Finix transfer");
        }
    }

    public async Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = new List<FinixAuthorization>();
            var seenCursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;

            do
            {
                var response = await _client.AuthorizationsApi.ListAuthorizations(
                    new ListAuthorizationsRequest
                    {
                        Merchant = MerchantId,
                        AfterCursor = cursor,
                        Limit = 100
                    },
                    null,
                    cancellationToken);

                if (response.Embedded?.Authorizations is { } authorizations)
                {
                    results.AddRange(authorizations.Where(authorization =>
                        authorization.CreatedAt is { } createdAt
                        && createdAt >= from
                        && createdAt <= to));
                }

                cursor = response.Page?.NextCursor;
                if (!string.IsNullOrWhiteSpace(cursor) && !seenCursors.Add(cursor))
                {
                    break;
                }
            } while (!string.IsNullOrWhiteSpace(cursor));

            return results;
        }
        catch (Exception exception) when (exception is ApiException or SdkException)
        {
            throw Wrap(exception, "Listing Finix authorizations");
        }
    }

    public async Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = new List<FinixTransfer>();
            var seenCursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;

            do
            {
                var response = await _client.TransfersApi.ListTransfers(
                    new ListTransfersRequest
                    {
                        Merchant = MerchantId,
                        AfterCursor = cursor,
                        Limit = 100
                    },
                    null,
                    cancellationToken);

                if (response.Embedded?.Transfers is { } transfers)
                {
                    results.AddRange(transfers.Where(transfer =>
                        transfer.CreatedAt is { } createdAt
                        && createdAt >= from
                        && createdAt <= to));
                }

                cursor = response.Page?.NextCursor;
                if (!string.IsNullOrWhiteSpace(cursor) && !seenCursors.Add(cursor))
                {
                    break;
                }
            } while (!string.IsNullOrWhiteSpace(cursor));

            return results;
        }
        catch (Exception exception) when (exception is ApiException or SdkException)
        {
            throw Wrap(exception, "Listing Finix transfers");
        }
    }

    private static Address? MapAddress(FinixBillingAddress? billingAddress)
    {
        if (billingAddress is null)
        {
            return null;
        }

        return new Address
        {
            Line1 = string.IsNullOrWhiteSpace(billingAddress.Line1) ? "N/A" : billingAddress.Line1,
            City = string.IsNullOrWhiteSpace(billingAddress.City) ? "N/A" : billingAddress.City,
            Region = string.IsNullOrWhiteSpace(billingAddress.Region) ? "NA" : billingAddress.Region,
            PostalCode = string.IsNullOrWhiteSpace(billingAddress.PostalCode) ? "00000" : billingAddress.PostalCode,
            Country = ToCountry(billingAddress.Country)
        };
    }

    private static Country ToCountry(string? value)
    {
        var code = (value ?? "USA").Trim().ToUpperInvariant();
        return code switch
        {
            "US" or "USA" => Country.Usa,
            "CA" or "CAN" => Country.Can,
            _ => JsonSerializer.Deserialize<Country>($"\"{code}\"", JsonOptions) ?? Country.Usa
        };
    }

    private static Currency ToCurrency(string value)
    {
        var code = value.Trim().ToUpperInvariant();
        return code switch
        {
            "USD" => Currency.Usd,
            "CAD" => Currency.Cad,
            _ => JsonSerializer.Deserialize<Currency>($"\"{code}\"", JsonOptions)
                 ?? Currency.Usd
        };
    }

    private static string NormalizeCardNumber(string number) =>
        new string((number ?? string.Empty).Where(char.IsDigit).ToArray());

    private static FinixGatewayException Wrap(Exception exception, string operation)
    {
        var status = exception is ApiException apiException ? apiException.StatusCode : HttpStatusCode.BadGateway;
        var detail = exception.Message;

        if (exception is ApiException)
        {
            var errorProperty = exception.GetType().GetProperty("Error");
            var error = errorProperty?.GetValue(exception);
            if (error is ApiError apiError && apiError.TryGetRawError(out var rawError))
            {
                detail = rawError.ReadAsString();
            }
            else if (error is not null)
            {
                try
                {
                    detail = JsonSerializer.Serialize(error, error.GetType(), JsonOptions);
                }
                catch (JsonException)
                {
                    detail = error.ToString() ?? detail;
                }
            }
        }

        return new FinixGatewayException($"{operation} failed ({(int)status} {status}): {detail}", status, exception);
    }
}



