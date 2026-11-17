using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using FinixApiReference.Core.Hooks;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Models.Enums;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class FinixGateway : IFinixGateway
{
    private readonly FinixApiReferenceClient _client;
    private readonly FinixSettings _settings;
    private readonly Currency _currency;

    public FinixGateway(IOptions<FinixSettings> settings, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _settings = settings.Value;
        ValidateSettings(_settings);

        _currency = ParseCurrency(_settings.Currency);

        var options = new FinixApiReferenceClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = _settings.Username,
                Password = _settings.Password
            },
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            },
            Hooks = new SdkHook[] { new FinixDateQueryHook() }
        };
        options.Server.Default.Production.BaseUrl = _settings.ResolveBaseUrl();

        var httpClient = httpClientFactory.CreateClient("Finix");
        httpClient.Timeout = TimeSpan.FromSeconds(100);
        _client = new FinixApiReferenceClient(httpClient, options);
    }

    public async Task<FinixApiReference.Models.Identity> CreateBuyerIdentityAsync(string buyerId, CancellationToken cancellationToken)
    {
        var email = buyerId.Contains('@') ? buyerId : $"{buyerId}@eshop.local";
        var localPart = email.Split('@')[0];
        var nameParts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : "eShop";
        var lastName = nameParts.Length > 1 ? nameParts[^1] : "Shopper";

        var identity = await _client.Identities.CreateIdentity(new CreateIdentityRequest
        {
            Body = IdentitiesRequest.Buyer(new Buyer
            {
                Entity = new Entity5
                {
                    Email = email,
                    FirstName = firstName,
                    LastName = lastName
                },
                IdentityRoles = new[] { IdentityRole5.Buyer },
                Type = Type10.Personal
            })
        }, cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(identity.Id))
        {
            throw new InvalidOperationException("Finix did not return an identity id.");
        }

        return identity;
    }

    public async Task<PaymentInstrumentCard> CreatePaymentInstrumentAsync(string identityId, FinixCardInput card, CancellationToken cancellationToken)
    {
        var result = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(new CreatePaymentInstrumentRequest
        {
            Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(new PaymentInstrumentCard1
            {
                Identity = identityId,
                Number = card.Number,
                ExpirationMonth = card.ExpirationMonth,
                ExpirationYear = card.ExpirationYear,
                SecurityCode = card.SecurityCode,
                Name = card.Name,
                Address = ToFinixAddress(card.Address)
            })
        }, cancellationToken: cancellationToken);

        if (!result.TryGetPaymentInstrumentCard(out var paymentInstrument) || paymentInstrument is null)
        {
            throw new InvalidOperationException("Finix did not return a card payment instrument.");
        }

        return paymentInstrument;
    }

    public async Task<Authorization> CreateAuthorizationAsync(long amountCents, string source, string idempotencyId, int orderId, string? securityCode, CancellationToken cancellationToken)
    {
        var authorization = await _client.AuthorizationsApi.CreateAuthorization(new CreateAuthorizationRequest
        {
            Body = AuthorizationsRequest.AuthorizationAllFields(new AuthorizationAllFields
            {
                Amount = amountCents,
                Currency = _currency,
                Merchant = _settings.MerchantId,
                Source = source,
                IdempotencyId = idempotencyId,
                SecurityCode = securityCode,
                Tags = new Dictionary<string, string>
                {
                    ["order_id"] = orderId.ToString(CultureInfo.InvariantCulture)
                }
            })
        }, cancellationToken: cancellationToken);

        return authorization;
    }

    public Task<Authorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken) =>
        _client.AuthorizationsApi.GetAuthorization(new GetAuthorizationRequest
        {
            AuthorizationId = authorizationId
        }, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<Authorization>> FindAuthorizationsByIdempotencyIdAsync(string idempotencyId, CancellationToken cancellationToken)
    {
        var page = await _client.AuthorizationsApi.ListAuthorizations(new ListAuthorizationsRequest
        {
            IdempotencyId = idempotencyId,
            CreatedAtGte = DateTimeOffset.UtcNow.AddDays(-1),
            Limit = 100
        }, cancellationToken: cancellationToken);

        return page.Embedded?.Authorizations ?? Array.Empty<Authorization>();
    }

    public Task<Authorization> CaptureAuthorizationAsync(string authorizationId, long amountCents, CancellationToken cancellationToken) =>
        _client.AuthorizationsApi.CaptureAuthorization(new CaptureAuthorizationRequest
        {
            AuthorizationId = authorizationId,
            Body = AuthorizationsRequest1.CaptureAuthorization(new CaptureAuthorization
            {
                CaptureAmount = amountCents
            })
        }, cancellationToken: cancellationToken);

    public Task<Authorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken) =>
        _client.AuthorizationsApi.VoidAuthorization(new VoidAuthorizationRequest
        {
            AuthorizationIdVoidTo = authorizationId,
            Body = new VoidAnAuthorization { VoidMe = true }
        }, cancellationToken: cancellationToken);

    public Task<Transfer> GetTransferAsync(string transferId, CancellationToken cancellationToken) =>
        _client.TransfersApi.GetTransfer(new GetTransferRequest
        {
            TransferId = transferId
        }, cancellationToken: cancellationToken);

    public Task<Transfer> CreateRefundAsync(string transferId, long amountCents, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (amountCents is <= 0 or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(amountCents));
        }

        var body = new RefundTransfer
        {
            RefundAmount = (int)amountCents
        };
        body.AdditionalProperties.Set("idempotency_id", idempotencyKey);

        return _client.TransfersApi.CreateTransferReversal(new CreateTransferReversalRequest
        {
            TransferId = transferId,
            Body = TransfersReversalsRequest.RefundTransfer(body)
        }, cancellationToken: cancellationToken);
    }

    public async Task<Transfer?> FindReversalByIdempotencyIdAsync(string transferId, string idempotencyKey, CancellationToken cancellationToken)
    {
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);

        do
        {
            var page = await _client.TransfersApi.ListTransferReversals(new ListTransferReversalsRequest
            {
                TransferId = transferId,
                Limit = 100,
                AfterCursor = cursor
            }, cancellationToken: cancellationToken);

            var match = page.Embedded?.Transfers?
                .FirstOrDefault(t => string.Equals(t.IdempotencyId, idempotencyKey, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }

            cursor = page.Page?.NextCursor;
            if (string.IsNullOrEmpty(cursor) || !seenCursors.Add(cursor))
            {
                cursor = null;
            }
        }
        while (cursor is not null);

        return null;
    }

    public async Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken)
    {
        await _client.PaymentInstrumentsApi.UpdatePaymentInstrument(new UpdatePaymentInstrumentRequest
        {
            PaymentInstrumentId = paymentInstrumentId,
            Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(new PaymentInstrumentDisableAPaymentInstrument
            {
                Enabled = false
            })
        }, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<Transfer>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<Transfer>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;

        do
        {
            var page = await _client.TransfersApi.ListTransfers(new ListTransfersRequest
            {
                CreatedAtGte = from,
                CreatedAtLte = to,
                Limit = 100,
                AfterCursor = cursor
            }, cancellationToken: cancellationToken);

            if (page.Embedded?.Transfers is { Count: > 0 } transfers)
            {
                results.AddRange(transfers);
            }

            cursor = page.Page?.NextCursor;
            if (string.IsNullOrEmpty(cursor) || !seenCursors.Add(cursor))
            {
                cursor = null;
            }
        }
        while (cursor is not null);

        return results;
    }

    public async Task<IReadOnlyList<Authorization>> ListAuthorizationsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<Authorization>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;

        do
        {
            var page = await _client.AuthorizationsApi.ListAuthorizations(new ListAuthorizationsRequest
            {
                CreatedAtGte = from,
                CreatedAtLte = to,
                Limit = 100,
                AfterCursor = cursor
            }, cancellationToken: cancellationToken);

            if (page.Embedded?.Authorizations is { Count: > 0 } authorizations)
            {
                results.AddRange(authorizations);
            }

            cursor = page.Page?.NextCursor;
            if (string.IsNullOrEmpty(cursor) || !seenCursors.Add(cursor))
            {
                cursor = null;
            }
        }
        while (cursor is not null);

        return results;
    }

    private static Address? ToFinixAddress(FinixAddressInput? address)
    {
        if (address is null)
        {
            return null;
        }

        return new Address
        {
            Line1 = address.Line1 ?? string.Empty,
            Line2 = address.Line2,
            City = address.City ?? string.Empty,
            Region = address.Region ?? string.Empty,
            PostalCode = address.PostalCode ?? string.Empty,
            Country = ParseCountry(address.Country)
        };
    }

    private static Country ParseCountry(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Country.Usa;
        }

        return JsonSerializer.Deserialize<Country>($"\"{code.Trim()}\"") ?? Country.Usa;
    }

    private static Currency ParseCurrency(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Finix:Currency is required.");
        }

        return JsonSerializer.Deserialize<Currency>($"\"{code.Trim()}\"")
            ?? throw new InvalidOperationException($"Finix:Currency '{code}' is not a valid ISO 4217 code.");
    }

    private static void ValidateSettings(FinixSettings settings)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.Username)) missing.Add("Finix:Username");
        if (string.IsNullOrWhiteSpace(settings.Password)) missing.Add("Finix:Password");
        if (string.IsNullOrWhiteSpace(settings.MerchantId)) missing.Add("Finix:MerchantId");
        if (string.IsNullOrWhiteSpace(settings.Environment)) missing.Add("Finix:Environment");
        if (string.IsNullOrWhiteSpace(settings.Currency)) missing.Add("Finix:Currency");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Missing Finix configuration: {string.Join(", ", missing)}.");
        }
    }

    private sealed class FinixDateQueryHook : SdkHook
    {
        public override ValueTask BeforeRequest(HttpRequestMessage request, HookContext context, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            if (uri is null || uri.Query.Length == 0)
            {
                return default;
            }

            var query = uri.Query.TrimStart('?');
            if (!query.Contains("created_at.", StringComparison.Ordinal))
            {
                return default;
            }

            var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                var separator = parts[i].IndexOf('=');
                if (separator < 0)
                {
                    continue;
                }

                var key = Uri.UnescapeDataString(parts[i][..separator]);
                if (!key.StartsWith("created_at.", StringComparison.Ordinal))
                {
                    continue;
                }

                var rawValue = Uri.UnescapeDataString(parts[i][(separator + 1)..]);
                if (!DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
                {
                    continue;
                }

                var normalized = value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                parts[i] = $"{key}={Uri.EscapeDataString(normalized)}";
            }

            request.RequestUri = new UriBuilder(uri) { Query = string.Join("&", parts) }.Uri;
            return default;
        }
    }
}
