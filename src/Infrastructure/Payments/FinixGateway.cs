using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.ErrorResponse;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Models.Enums;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class FinixGateway : IFinixGateway
{
    private static readonly Regex DuplicateAuthorizationRegex =
        new(@"Duplicate authorization\s+(\S+)\s+already exists", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly FinixApiReferenceClient _client;
    private readonly ILogger<FinixGateway> _logger;

    public FinixGateway(IOptions<FinixOptions> options, ILogger<FinixGateway> logger)
    {
        _logger = logger;
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.Username)
            || string.IsNullOrWhiteSpace(value.Password)
            || string.IsNullOrWhiteSpace(value.MerchantId)
            || string.IsNullOrWhiteSpace(value.Currency))
        {
            throw new PaymentValidationException(
                "Finix is not configured. Set Finix:Username, Finix:Password, Finix:MerchantId and Finix:Currency (plus Finix:Environment or Finix:BaseUrl).");
        }

        var clientOptions = new FinixApiReferenceClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = value.Username,
                Password = value.Password
            }
        };
        clientOptions.Server.Default.Production.BaseUrl = value.ResolveBaseUrl();
        _client = new FinixApiReferenceClient(new System.Net.Http.HttpClient(), clientOptions);
    }

    public async Task<FinixIdentity> CreateBuyerIdentityAsync(string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken)
    {
        var request = new CreateIdentityRequest
        {
            Body = IdentitiesRequest.Buyer(new Buyer
            {
                Entity = new Entity5
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email
                },
                IdentityRoles = new[] { IdentityRole5.Buyer },
                Type = Type10.Personal
            })
        };

        try
        {
            var identity = await _client.Identities.CreateIdentity(request, null, cancellationToken);
            if (string.IsNullOrWhiteSpace(identity.Id))
            {
                throw new PaymentGatewayException("Finix did not return an identity id.");
            }

            return new FinixIdentity(identity.Id);
        }
        catch (ApiException ex)
        {
            throw new PaymentGatewayException(Describe(ex));
        }
        catch (SdkException ex)
        {
            throw new PaymentGatewayException(ex.Message);
        }
    }

    public async Task<FinixCard> CreateCardAsync(string identityId, CardData card, CancellationToken cancellationToken)
    {
        var request = new CreatePaymentInstrumentRequest
        {
            Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(new PaymentInstrumentCard1
            {
                Identity = identityId,
                Name = card.Name,
                Number = NormalizeCardNumber(card.Number),
                ExpirationMonth = card.ExpirationMonth,
                ExpirationYear = card.ExpirationYear,
                SecurityCode = card.Cvc,
                Address = MapAddress(card.Address)
            })
        };

        try
        {
            var response = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(request, null, cancellationToken);
            if (!response.TryGetPaymentInstrumentCard(out var paymentInstrument) ||
                string.IsNullOrWhiteSpace(paymentInstrument.Id))
            {
                throw new PaymentGatewayException("Finix did not return a card payment instrument.");
            }

            return MapCard(paymentInstrument, identityId);
        }
        catch (ApiException ex)
        {
            throw new PaymentGatewayException(Describe(ex));
        }
        catch (SdkException ex)
        {
            throw new PaymentGatewayException(ex.Message);
        }
    }

    public async Task DisableCardAsync(string paymentInstrumentId, CancellationToken cancellationToken)
    {
        var request = new UpdatePaymentInstrumentRequest
        {
            PaymentInstrumentId = paymentInstrumentId,
            Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                new PaymentInstrumentDisableAPaymentInstrument { Enabled = false })
        };

        try
        {
            await _client.PaymentInstrumentsApi.UpdatePaymentInstrument(request, null, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning("Finix could not disable payment instrument {PaymentInstrumentId}: {Message}",
                paymentInstrumentId,
                Describe(ex));
        }
        catch (SdkException ex)
        {
            _logger.LogWarning("Finix could not disable payment instrument {PaymentInstrumentId}: {Message}",
                paymentInstrumentId,
                ex.Message);
        }
    }

    public async Task<FinixAuthorization> AuthorizeAsync(string merchantId,
        string currency,
        long amountCents,
        string source,
        string idempotencyId,
        string? identityId,
        CancellationToken cancellationToken)
    {
        var request = new CreateAuthorizationRequest
        {
            Body = AuthorizationsRequest.AuthorizationAllFields(new AuthorizationAllFields
            {
                Amount = amountCents,
                Currency = ParseCurrency(currency),
                Merchant = merchantId,
                Source = source,
                IdempotencyId = idempotencyId,
                Identity = identityId
            })
        };

        try
        {
            var authorization = await _client.AuthorizationsApi.CreateAuthorization(request, null, cancellationToken);
            return MapAuthorization(authorization);
        }
        catch (ApiException ex)
        {
            var description = Describe(ex);
            var match = DuplicateAuthorizationRegex.Match(description);
            if (match.Success)
            {
                throw new FinixDuplicateAuthorizationException(match.Groups[1].Value, description);
            }

            throw new PaymentGatewayException(description);
        }
        catch (SdkException ex)
        {
            throw new PaymentGatewayException(ex.Message);
        }
    }

    public async Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId },
                null,
                cancellationToken);
            return MapAuthorization(authorization);
        }
        catch (ApiException ex)
        {
            throw new PaymentGatewayException(Describe(ex));
        }
        catch (SdkException ex)
        {
            throw new PaymentGatewayException(ex.Message);
        }
    }

    public async Task<FinixAuthorization> CaptureAsync(string authorizationId,
        long amountCents,
        CancellationToken cancellationToken)
    {
        var request = new CaptureAuthorizationRequest
        {
            AuthorizationId = authorizationId,
            Body = AuthorizationsRequest1.CaptureAuthorization(new CaptureAuthorization
            {
                CaptureAmount = amountCents
            })
        };

        try
        {
            var authorization = await _client.AuthorizationsApi.CaptureAuthorization(request, null, cancellationToken);
            return MapAuthorization(authorization);
        }
        catch (ApiException ex)
        {
            throw new PaymentGatewayException(Describe(ex));
        }
        catch (SdkException ex)
        {
            throw new PaymentGatewayException(ex.Message);
        }
    }

    public async Task<FinixAuthorization> VoidAsync(string authorizationId, CancellationToken cancellationToken)
    {
        var request = new VoidAuthorizationRequest
        {
            AuthorizationIdVoidTo = authorizationId,
            Body = new VoidAnAuthorization { VoidMe = true }
        };

        try
        {
            var authorization = await _client.AuthorizationsApi.VoidAuthorization(request, null, cancellationToken);
            return MapAuthorization(authorization);
        }
        catch (ApiException ex)
        {
            throw new PaymentGatewayException(Describe(ex));
        }
        catch (SdkException ex)
        {
            throw new PaymentGatewayException(ex.Message);
        }
    }

    public async Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        try
        {
            var transfer = await _client.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = transferId },
                null,
                cancellationToken);
            return MapTransfer(transfer);
        }
        catch (ApiException ex)
        {
            throw new PaymentGatewayException(Describe(ex));
        }
        catch (SdkException ex)
        {
            throw new PaymentGatewayException(ex.Message);
        }
    }

    public async Task<FinixTransfer> RefundAsync(string transferId, long amountCents, CancellationToken cancellationToken)
    {
        var request = new CreateTransferReversalRequest
        {
            TransferId = transferId,
            Body = TransfersReversalsRequest.RefundTransfer(new RefundTransfer
            {
                RefundAmount = (int)amountCents
            })
        };

        try
        {
            var transfer = await _client.TransfersApi.CreateTransferReversal(request, null, cancellationToken);
            return MapTransfer(transfer);
        }
        catch (ApiException ex)
        {
            throw new PaymentGatewayException(Describe(ex));
        }
        catch (SdkException ex)
        {
            throw new PaymentGatewayException(ex.Message);
        }
    }

    public async Task<FinixTransferListing> ListAllTransfersAsync(string merchantId,
        CancellationToken cancellationToken)
    {
        var transfers = new List<FinixTransfer>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var pages = 0;
        string? cursor = null;

        do
        {
            ListTransfers page;
            try
            {
                page = await _client.TransfersApi.ListTransfers(new ListTransfersRequest
                {
                    Merchant = merchantId,
                    Limit = 100,
                    AfterCursor = cursor
                }, null, cancellationToken);
            }
            catch (ApiException ex)
            {
                throw new PaymentGatewayException(Describe(ex));
            }
            catch (SdkException ex)
            {
                throw new PaymentGatewayException(ex.Message);
            }

            pages++;
            if (page.Embedded?.Transfers is not null)
            {
                transfers.AddRange(page.Embedded.Transfers.Select(MapTransfer));
            }

            var next = page.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(next) || !cursors.Add(next))
            {
                break;
            }

            cursor = next;
        } while (true);

        return new FinixTransferListing(transfers, pages);
    }

    public async Task<FinixAuthorizationListing> ListAllAuthorizationsAsync(string merchantId,
        CancellationToken cancellationToken)
    {
        var authorizations = new List<FinixAuthorization>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var pages = 0;
        string? cursor = null;

        do
        {
            ListAuthorizations page;
            try
            {
                page = await _client.AuthorizationsApi.ListAuthorizations(new ListAuthorizationsRequest
                {
                    Merchant = merchantId,
                    Limit = 100,
                    AfterCursor = cursor
                }, null, cancellationToken);
            }
            catch (ApiException ex)
            {
                throw new PaymentGatewayException(Describe(ex));
            }
            catch (SdkException ex)
            {
                throw new PaymentGatewayException(ex.Message);
            }

            pages++;
            if (page.Embedded?.Authorizations is not null)
            {
                authorizations.AddRange(page.Embedded.Authorizations.Select(MapAuthorization));
            }

            var next = page.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(next) || !cursors.Add(next))
            {
                break;
            }

            cursor = next;
        } while (true);

        return new FinixAuthorizationListing(authorizations, pages);
    }

    private static FinixCard MapCard(PaymentInstrumentCard card, string identityId) =>
        new(card.Id!,
            card.Brand?.Value,
            card.LastFour,
            card.ExpirationMonth,
            card.ExpirationYear,
            card.Name,
            card.Identity ?? identityId,
            card.Enabled);

    private static FinixAuthorization MapAuthorization(Authorization authorization) =>
        new(authorization.Id!,
            authorization.State?.Value,
            authorization.Amount,
            authorization.Transfer,
            authorization.IsVoid,
            authorization.ExpiresAt,
            authorization.FailureCode,
            authorization.FailureMessage,
            authorization.CreatedAt,
            authorization.Merchant);

    private static FinixTransfer MapTransfer(Transfer transfer) =>
        new(transfer.Id!,
            transfer.Type?.Value,
            transfer.State?.Value,
            transfer.Amount,
            transfer.Fee,
            transfer.ParentTransfer,
            transfer.CreatedAt,
            transfer.FailureCode,
            transfer.FailureMessage,
            transfer.Merchant);

    private static string NormalizeCardNumber(string number) =>
        new(number.Where(char.IsDigit).ToArray());

    private static Address MapAddress(AddressData? address)
    {
        var data = address ?? new AddressData("1 Main St", "San Francisco", "CA", "USA", "94105");
        return new Address
        {
            Line1 = string.IsNullOrWhiteSpace(data.Street) ? "1 Main St" : data.Street,
            City = string.IsNullOrWhiteSpace(data.City) ? "San Francisco" : data.City,
            Region = string.IsNullOrWhiteSpace(data.State) ? "CA" : data.State,
            PostalCode = string.IsNullOrWhiteSpace(data.ZipCode) ? "94105" : data.ZipCode,
            Country = ParseCountry(data.Country)
        };
    }

    private static Country ParseCountry(string? country)
    {
        var value = string.IsNullOrWhiteSpace(country) ? "USA" : country.Trim().ToUpperInvariant();
        if (value == "US")
        {
            value = "USA";
        }

        return Country.TryGetKnownValue(value, out var known) ? known : Country.Usa;
    }

    private static Currency ParseCurrency(string currency) =>
        JsonSerializer.Deserialize<Currency>(JsonSerializer.Serialize(currency))
        ?? throw new PaymentValidationException($"Finix currency '{currency}' is not valid.");

    private static string Describe(ApiException exception)
    {
        var error = exception.GetType().GetProperty("Error")?.GetValue(exception);
        if (error is ApiError apiError)
        {
            var tryGetGenericError = apiError.GetType().GetMethod("TryGetGenericError");
            if (tryGetGenericError is not null)
            {
                var arguments = new object?[] { null };
                if (tryGetGenericError.Invoke(apiError, arguments) is true && arguments[0] is GenericError genericError)
                {
                    var messages = genericError.Embedded?.Errors?
                        .Select(error => error.Message)
                        .Where(message => !string.IsNullOrWhiteSpace(message))
                        .ToArray();
                    if (messages is { Length: > 0 })
                    {
                        return string.Join("; ", messages!);
                    }
                }
            }

            if (apiError.TryGetRawError(out var rawError))
            {
                var raw = rawError.ReadAsString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    return raw;
                }
            }
        }

        return exception.Message;
    }
}
