using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.ErrorResponse;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Models.Enums;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.FeesApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using FinixAddress = FinixApiReference.Models.Address;
using FinixAuthorization = FinixApiReference.Models.Authorization;
using FinixCountry = FinixApiReference.Models.Enums.Country;
using FinixCurrency = FinixApiReference.Models.Enums.Currency;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// All Finix calls in the application go through this gateway, and every call is
/// made through the Finix C# SDK. The gateway keeps Finix-specific types out of
/// the application and API layers.
/// </summary>
public sealed class FinixPaymentGateway : IFinixPaymentGateway
{
    private const int PageSize = 100;
    private const int MaxPages = 1000;

    private readonly FinixApiReferenceClient _client;
    private readonly FinixOptions _options;

    public FinixPaymentGateway(FinixApiReferenceClient client, FinixOptions options)
    {
        _client = client;
        _options = options;
    }

    public string Currency => _options.Currency;

    public string MerchantId => _options.MerchantId;

    public async Task<FinixIdentityResult> CreateBuyerIdentityAsync(string buyerId, string? name,
        PaymentAddress? address, CancellationToken cancellationToken)
    {
        var (firstName, lastName) = SplitName(name);
        var email = buyerId.Contains('@') ? buyerId : $"{SanitizeEmailLocalPart(buyerId)}@eshop.local";

        var buyer = new Buyer
        {
            Entity = new Entity5
            {
                Email = Truncate(email, 100),
                FirstName = firstName,
                LastName = lastName,
                PersonalAddress = ToFinixAddress(address)
            },
            IdentityRoles = new[] { IdentityRole5.Buyer },
            Type = Type10.Personal
        };

        try
        {
            var identity = await _client.Identities.CreateIdentity(
                new CreateIdentityRequest { Body = IdentitiesRequest.Buyer(buyer) },
                cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(identity.Id))
            {
                throw new FinixGatewayException("Finix did not return an identity id.");
            }

            return new FinixIdentityResult { IdentityId = identity.Id };
        }
        catch (Exception ex) when (ex is not FinixGatewayException)
        {
            throw Translate(ex, "Creating the Finix buyer identity");
        }
    }

    public async Task<FinixCardResult> CreateCardAsync(string finixIdentityId, PaymentCardDetails card,
        CancellationToken cancellationToken)
    {
        var body = new PaymentInstrumentCard1
        {
            Address = ToFinixAddress(card.Address),
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            Identity = finixIdentityId,
            Name = Truncate(string.IsNullOrWhiteSpace(card.Name) ? "eShop Shopper" : card.Name.Trim(), 100),
            Number = NormalizeCardNumber(card.Number),
            SecurityCode = card.SecurityCode,
            Tags = new Dictionary<string, string> { ["source"] = "eshop" }
        };

        try
        {
            var response = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(
                new CreatePaymentInstrumentRequest
                {
                    Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(body)
                },
                cancellationToken: cancellationToken);

            if (!response.TryGetPaymentInstrumentCard(out var instrument) ||
                string.IsNullOrWhiteSpace(instrument.Id))
            {
                throw new FinixGatewayException("Finix did not return a card payment instrument.");
            }

            return MapCard(instrument);
        }
        catch (Exception ex) when (ex is not FinixGatewayException)
        {
            throw Translate(ex, "Tokenizing the card with Finix");
        }
    }

    public async Task<FinixCardResult> GetCardAsync(string paymentInstrumentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.PaymentInstrumentsApi.GetPaymentInstrument(
                new GetPaymentInstrumentRequest { PaymentInstrumentId = paymentInstrumentId },
                cancellationToken: cancellationToken);

            if (!response.TryGetPaymentInstrumentCard(out var instrument))
            {
                throw new FinixGatewayException(
                    "The Finix payment instrument is not a card and cannot be used for card payments.",
                    code: "NOT_A_CARD");
            }

            return MapCard(instrument);
        }
        catch (Exception ex) when (ex is not FinixGatewayException)
        {
            throw Translate(ex, "Reading the saved card from Finix");
        }
    }

    public async Task DisableCardAsync(string paymentInstrumentId, CancellationToken cancellationToken)
    {
        try
        {
            var body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                new PaymentInstrumentDisableAPaymentInstrument { Enabled = false });

            await _client.PaymentInstrumentsApi.UpdatePaymentInstrument(
                new UpdatePaymentInstrumentRequest
                {
                    PaymentInstrumentId = paymentInstrumentId,
                    Body = body
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone at Finix; the local record is still removed.
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Disabling the saved card at Finix");
        }
    }

    public async Task<FinixAuthorizationResult> AuthorizeAsync(string paymentInstrumentId,
        long amountInCents, string currency, string idempotencyKey, int orderId, string buyerId,
        CancellationToken cancellationToken)
    {
        var body = new AuthorizationAllFields
        {
            Amount = amountInCents,
            Currency = ToFinixCurrency(currency),
            Merchant = _options.MerchantId,
            Source = paymentInstrumentId,
            IdempotencyId = idempotencyKey,
            StatementDescriptor = "ESHOP",
            Tags = new Dictionary<string, string>
            {
                ["eshop_order_id"] = orderId.ToString(CultureInfo.InvariantCulture),
                ["eshop_buyer_id"] = SanitizeTag(buyerId),
                ["eshop_kind"] = "authorization"
            }
        };

        try
        {
            var authorization = await _client.AuthorizationsApi.CreateAuthorization(
                new CreateAuthorizationRequest
                {
                    Body = AuthorizationsRequest.AuthorizationAllFields(body)
                },
                cancellationToken: cancellationToken);

            return MapAuthorization(authorization);
        }
        catch (Exception ex) when (IsDuplicateIdempotency(ex))
        {
            // A previous call may have succeeded even though the response or
            // local save was lost. Recover the existing authorization instead
            // of treating the duplicate key as a new decline.
            var existing = await FindAuthorizationByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw Translate(ex, "Authorizing the order with Finix");
        }
        catch (Exception ex) when (ex is not FinixGatewayException)
        {
            throw Translate(ex, "Authorizing the order with Finix");
        }
    }

    public async Task<FinixAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId },
                cancellationToken: cancellationToken);

            return MapAuthorization(authorization);
        }
        catch (Exception ex) when (ex is not FinixGatewayException)
        {
            throw Translate(ex, "Reading the authorization from Finix");
        }
    }

    public async Task<FinixCaptureResult> CaptureAsync(string authorizationId, long amountInCents,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var body = new CaptureAuthorization { CaptureAmount = amountInCents };
        body.AdditionalProperties.Set("idempotency_id", idempotencyKey);

        try
        {
            var authorization = await _client.AuthorizationsApi.CaptureAuthorization(
                new CaptureAuthorizationRequest
                {
                    AuthorizationId = authorizationId,
                    Body = AuthorizationsRequest1.CaptureAuthorization(body)
                },
                cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(authorization.Transfer))
            {
                throw new FinixGatewayException(
                    "Finix captured the authorization but did not return the capture transfer id.");
            }

            return await ReadCaptureAsync(authorization.Transfer, amountInCents, true, cancellationToken);
        }
        catch (Exception ex) when (ex is not FinixGatewayException)
        {
            throw Translate(ex, "Capturing the authorization with Finix");
        }
    }

    public Task<FinixCaptureResult> GetCaptureAsync(string captureTransferId,
        CancellationToken cancellationToken) =>
        ReadCaptureAsync(captureTransferId, 0, false, cancellationToken);

    private async Task<FinixCaptureResult> ReadCaptureAsync(string transferId, long fallbackAmount,
        bool waitForFees, CancellationToken cancellationToken)
    {
        Transfer transfer = await _client.TransfersApi.GetTransfer(
            new GetTransferRequest { TransferId = transferId },
            cancellationToken: cancellationToken);

        var fees = await ListFeesForTransferAsync(transferId, cancellationToken);

        // Finix processes the capture asynchronously. The transfer can start in
        // PENDING and the fee records are created when processing completes, so
        // wait for a terminal state and the fee records before reporting figures.
        if (waitForFees)
        {
            for (var attempt = 0; attempt < 60; attempt++)
            {
                var terminal = !string.Equals(transfer.State?.Value, "PENDING",
                    StringComparison.OrdinalIgnoreCase);
                if (terminal && fees.Count > 0)
                {
                    break;
                }

                // A merchant without a fee profile may legitimately report no
                // fee records; do not wait the full window for that case.
                if (terminal && attempt >= 20)
                {
                    break;
                }

                await Task.Delay(500, cancellationToken);
                transfer = await _client.TransfersApi.GetTransfer(
                    new GetTransferRequest { TransferId = transferId },
                    cancellationToken: cancellationToken);
                fees = await ListFeesForTransferAsync(transferId, cancellationToken);
            }
        }

        var feeInCents = fees.Sum(f => f.AmountInCents);
        var captured = transfer.Amount ?? fallbackAmount;

        return new FinixCaptureResult
        {
            TransferId = transfer.Id ?? transferId,
            TransferState = transfer.State?.Value,
            AmountInCents = captured,
            FeeInCents = feeInCents,
            NetAmountInCents = captured - feeInCents,
            FailureCode = transfer.FailureCode,
            FailureMessage = transfer.FailureMessage
        };
    }

    public async Task<FinixAuthorizationResult> VoidAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await _client.AuthorizationsApi.VoidAuthorization(
                new VoidAuthorizationRequest
                {
                    AuthorizationIdVoidTo = authorizationId,
                    Body = new VoidAnAuthorization { VoidMe = true }
                },
                cancellationToken: cancellationToken);

            return MapAuthorization(authorization);
        }
        catch (Exception ex) when (ex is not FinixGatewayException)
        {
            throw Translate(ex, "Releasing the authorization hold with Finix");
        }
    }

    public async Task<FinixRefundResult> RefundAsync(string captureTransferId, long amountInCents,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var body = new RefundTransfer { RefundAmount = checked((int)amountInCents) };
        body.AdditionalProperties.Set("idempotency_id", idempotencyKey);

        try
        {
            var transfer = await _client.TransfersApi.CreateTransferReversal(
                new CreateTransferReversalRequest
                {
                    TransferId = captureTransferId,
                    Body = TransfersReversalsRequest.RefundTransfer(body)
                },
                cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(transfer.Id))
            {
                throw new FinixGatewayException("Finix did not return a refund transfer id.");
            }

            return new FinixRefundResult
            {
                RefundTransferId = transfer.Id!,
                State = transfer.State?.Value,
                AmountInCents = transfer.Amount ?? amountInCents
            };
        }
        catch (Exception ex) when (IsDuplicateIdempotency(ex))
        {
            var existing = await FindRefundByIdempotencyKeyAsync(captureTransferId, idempotencyKey,
                cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw Translate(ex, "Refunding the captured payment with Finix");
        }
        catch (Exception ex) when (ex is not FinixGatewayException)
        {
            throw Translate(ex, "Refunding the captured payment with Finix");
        }
    }

    public async Task<IReadOnlyList<FinixTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var records = new List<FinixTransactionRecord>();
        await AddAuthorizationsAsync(records, from, to, cancellationToken);
        await AddTransfersAsync(records, from, to, cancellationToken);

        return records
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<FinixFeeRecord>> ListFeesForTransferAsync(string transferId,
        CancellationToken cancellationToken)
    {
        var fees = new List<FinixFeeRecord>();
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            ListFees response;
            try
            {
                response = await _client.FeesApi.ListFees(new ListFeesRequest
                {
                    AfterCursor = cursor,
                    Limit = PageSize,
                    LinkedTo = transferId,
                    MerchantId = _options.MerchantId
                }, cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not FinixGatewayException)
            {
                throw Translate(ex, "Reading Finix fees");
            }

            foreach (var fee in response.Embedded?.Fees ?? Array.Empty<Fee>())
            {
                fees.Add(new FinixFeeRecord
                {
                    Id = fee.Id ?? string.Empty,
                    AmountInCents = (long)Math.Round(fee.Amount ?? 0, MidpointRounding.AwayFromZero),
                    Category = fee.Category?.Value,
                    FeeType = fee.FeeType?.Value,
                    LinkedTo = fee.LinkedTo ?? fee.LinkedId
                });
            }

            var next = response.Page?.NextCursor;
            if (string.IsNullOrEmpty(next) || next == cursor)
            {
                break;
            }

            cursor = next;
        }

        return fees
            .GroupBy(f => f.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
    }

    private async Task AddAuthorizationsAsync(List<FinixTransactionRecord> records, DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            ListAuthorizations response;
            try
            {
                response = await _client.AuthorizationsApi.ListAuthorizations(new ListAuthorizationsRequest
                {
                    AfterCursor = cursor,
                    Limit = PageSize,
                    Merchant = _options.MerchantId,
                    CreatedAtGte = from,
                    CreatedAtLte = to
                }, cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not FinixGatewayException)
            {
                throw Translate(ex, "Listing Finix authorizations");
            }

            foreach (var authorization in response.Embedded?.Authorizations ?? Array.Empty<FinixAuthorization>())
            {
                records.Add(new FinixTransactionRecord
                {
                    Id = authorization.Id ?? string.Empty,
                    Kind = "AUTHORIZATION",
                    Type = "AUTHORIZATION",
                    State = authorization.State?.Value,
                    AmountInCents = authorization.Amount ?? 0,
                    Currency = authorization.Currency?.Value,
                    CreatedAt = authorization.CreatedAt,
                    ParentTransferId = authorization.Transfer
                });
            }

            var next = response.Page?.NextCursor;
            if (string.IsNullOrEmpty(next) || next == cursor)
            {
                break;
            }

            cursor = next;
        }
    }

    private async Task AddTransfersAsync(List<FinixTransactionRecord> records, DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            ListTransfers response;
            try
            {
                response = await _client.TransfersApi.ListTransfers(new ListTransfersRequest
                {
                    AfterCursor = cursor,
                    Limit = PageSize,
                    Merchant = _options.MerchantId,
                    CreatedAtGte = from,
                    CreatedAtLte = to
                }, cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not FinixGatewayException)
            {
                throw Translate(ex, "Listing Finix transfers");
            }

            foreach (var transfer in response.Embedded?.Transfers ?? Array.Empty<Transfer>())
            {
                records.Add(new FinixTransactionRecord
                {
                    Id = transfer.Id ?? string.Empty,
                    Kind = "TRANSFER",
                    Type = transfer.Type?.Value,
                    State = transfer.State?.Value,
                    AmountInCents = transfer.Amount ?? 0,
                    Currency = transfer.Currency?.Value,
                    CreatedAt = transfer.CreatedAt,
                    ParentTransferId = transfer.ParentTransfer
                });
            }

            var next = response.Page?.NextCursor;
            if (string.IsNullOrEmpty(next) || next == cursor)
            {
                break;
            }

            cursor = next;
        }
    }

    private static FinixAuthorizationResult MapAuthorization(FinixAuthorization authorization) =>
        new()
        {
            AuthorizationId = authorization.Id ?? string.Empty,
            State = authorization.State?.Value,
            AmountInCents = authorization.Amount ?? 0,
            ExpiresAt = authorization.ExpiresAt,
            TransferId = authorization.Transfer,
            IsVoid = authorization.IsVoid ?? false,
            FailureCode = authorization.FailureCode,
            FailureMessage = authorization.FailureMessage
        };

    private static FinixCardResult MapCard(PaymentInstrumentCard instrument) =>
        new()
        {
            PaymentInstrumentId = instrument.Id ?? string.Empty,
            Brand = instrument.Brand?.Value,
            LastFour = instrument.LastFour,
            ExpirationMonth = instrument.ExpirationMonth,
            ExpirationYear = instrument.ExpirationYear,
            CardType = instrument.CardType?.Value,
            Name = instrument.Name,
            Enabled = instrument.Enabled ?? true
        };

    private static FinixCurrency ToFinixCurrency(string currency)
    {
        if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
        {
            return FinixCurrency.Usd;
        }

        if (string.Equals(currency, "CAD", StringComparison.OrdinalIgnoreCase))
        {
            return FinixCurrency.Cad;
        }

        throw new FinixGatewayException($"Finix does not support the configured currency '{currency}'.");
    }

    private static FinixAddress? ToFinixAddress(PaymentAddress? address)
    {
        if (address is null ||
            string.IsNullOrWhiteSpace(address.Line1) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Region) ||
            string.IsNullOrWhiteSpace(address.PostalCode))
        {
            return null;
        }

        return new FinixAddress
        {
            Line1 = Truncate(address.Line1.Trim(), 35),
            Line2 = string.IsNullOrWhiteSpace(address.Line2) ? null : Truncate(address.Line2.Trim(), 35),
            City = Truncate(address.City.Trim(), 20),
            Region = NormalizeRegion(address.Region),
            PostalCode = Truncate(address.PostalCode.Trim(), 7),
            Country = ToFinixCountry(address.Country)
        };
    }

    private static FinixCountry ToFinixCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return FinixCountry.Usa;
        }

        var normalized = country.Trim().ToUpperInvariant();
        return normalized is "CAN" or "CANADA" or "CA"
            ? FinixCountry.Can
            : FinixCountry.Usa;
    }

    private static string NormalizeRegion(string region)
    {
        var trimmed = region.Trim().ToUpperInvariant();
        return trimmed.Length <= 2 ? trimmed : trimmed[..2];
    }

    private static string NormalizeCardNumber(string number) =>
        new string((number ?? string.Empty).Where(char.IsDigit).ToArray());

    private static (string FirstName, string LastName) SplitName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ("eShop", "Shopper");
        }

        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return (Truncate(parts[0], 20), "Shopper");
        }

        return (Truncate(parts[0], 20), Truncate(parts[^1], 20));
    }

    private static string SanitizeEmailLocalPart(string value)
    {
        var sanitized = new string(value.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray());
        return string.IsNullOrEmpty(sanitized) ? "shopper" : sanitized;
    }

    private static string SanitizeTag(string value)
    {
        var sanitized = value
            .Replace("\\", "_", StringComparison.Ordinal)
            .Replace(",", "_", StringComparison.Ordinal)
            .Replace("\"", "_", StringComparison.Ordinal)
            .Replace("'", "_", StringComparison.Ordinal);
        return Truncate(sanitized, 500);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private async Task<FinixAuthorizationResult?> FindAuthorizationByIdempotencyKeyAsync(string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await _client.AuthorizationsApi.ListAuthorizations(new ListAuthorizationsRequest
            {
                IdempotencyId = idempotencyKey,
                Merchant = _options.MerchantId,
                Limit = 10
            }, cancellationToken: cancellationToken);

            var authorization = page.Embedded?.Authorizations?
                .FirstOrDefault(a => string.Equals(a.IdempotencyId, idempotencyKey, StringComparison.Ordinal));

            return authorization is null ? null : MapAuthorization(authorization);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<FinixRefundResult?> FindRefundByIdempotencyKeyAsync(string captureTransferId,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            string? cursor = null;
            for (var pageNumber = 0; pageNumber < MaxPages; pageNumber++)
            {
                var page = await _client.TransfersApi.ListTransferReversals(new ListTransferReversalsRequest
                {
                    TransferId = captureTransferId,
                    AfterCursor = cursor,
                    Limit = PageSize
                }, cancellationToken: cancellationToken);

                var transfer = page.Embedded?.Transfers?
                    .FirstOrDefault(t => string.Equals(t.IdempotencyId, idempotencyKey, StringComparison.Ordinal));

                if (transfer is not null)
                {
                    return new FinixRefundResult
                    {
                        RefundTransferId = transfer.Id ?? string.Empty,
                        State = transfer.State?.Value,
                        AmountInCents = transfer.Amount ?? 0
                    };
                }

                var next = page.Page?.NextCursor;
                if (string.IsNullOrEmpty(next) || next == cursor)
                {
                    break;
                }

                cursor = next;
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    private static bool IsDuplicateIdempotency(Exception exception)
    {
        if (exception is not ApiException apiException)
        {
            return false;
        }

        var (code, message) = ExtractApiError(apiException);
        return ContainsAny(code, "IDEMPOTENCY", "DUPLICATE") ||
               ContainsAny(message, "idempotency", "duplicate");
    }
    private static FinixGatewayException Translate(Exception exception, string operation)
    {
        if (exception is ApiException apiException)
        {
            var (code, message) = ExtractApiError(apiException);
            var detail = RedactCardNumbers(string.IsNullOrWhiteSpace(message) ? apiException.Message : message);
            var isDecline = apiException.StatusCode == HttpStatusCode.PaymentRequired ||
                            ContainsAny(code, "DECLINE", "INSUFFICIENT_FUNDS", "DO_NOT_HONOR", "CARD_DECLINED");
            var isHold = ContainsAny(code, "EXPIRED", "CANCELED", "CANCELLED", "VOID", "AUTHORIZATION");
            var fullMessage = $"{operation} failed ({(int)apiException.StatusCode} {apiException.StatusCode}): {detail}";

            return new FinixGatewayException(fullMessage, isDecline, isHold, code, exception);
        }

        if (exception is SdkTimeoutException timeout)
        {
            return new FinixGatewayException(
                $"{operation} timed out after {timeout.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds. The outcome is unknown; retry with the same idempotency key.",
                innerException: exception);
        }

        if (exception is SdkConnectionException)
        {
            return new FinixGatewayException(
                $"{operation} could not reach Finix. The outcome is unknown; retry with the same idempotency key.",
                innerException: exception);
        }

        return new FinixGatewayException($"{operation} failed: {RedactCardNumbers(exception.Message)}", innerException: exception);
    }

    private static (string? Code, string? Message) ExtractApiError(ApiException exception)
    {
        var error = exception.GetType()
            .GetProperty("Error", BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(exception);

        if (error is null)
        {
            return (null, null);
        }

        var generic = TryInvokeOut<GenericError>(error, "TryGetGenericError");
        if (generic is not null)
        {
            var first = generic.Embedded?.Errors?.FirstOrDefault();
            return (first?.Code, first?.Message);
        }

        var raw = TryInvokeOut<RawError>(error, "TryGetRawError");
        if (raw is not null)
        {
            var text = raw.ReadAsString();
            try
            {
                using var document = JsonDocument.Parse(text);
                return (FindFirstString(document.RootElement, "code"),
                    FindFirstString(document.RootElement, "message") ?? Truncate(text, 1000));
            }
            catch (JsonException)
            {
                return (null, Truncate(text, 1000));
            }
        }

        try
        {
            var json = JsonSerializer.Serialize(error, error.GetType());
            using var document = JsonDocument.Parse(json);
            return (FindFirstString(document.RootElement, "code"),
                FindFirstString(document.RootElement, "message"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static T? TryInvokeOut<T>(object target, string methodName) where T : class
    {
        var method = target.GetType().GetMethod(methodName,
            BindingFlags.Public | BindingFlags.Instance);
        if (method is null)
        {
            return null;
        }

        var arguments = new object?[] { null };
        var succeeded = method.Invoke(target, arguments) is true;
        return succeeded ? arguments[0] as T : null;
    }
    private static string? FindFirstString(JsonElement element, string propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString();
                    }

                    var nested = FindFirstString(property.Value, propertyName);
                    if (!string.IsNullOrEmpty(nested))
                    {
                        return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindFirstString(item, propertyName);
                    if (!string.IsNullOrEmpty(nested))
                    {
                        return nested;
                    }
                }

                break;
        }

        return null;
    }

    private static string RedactCardNumbers(string value) =>
        Regex.Replace(value, @"\b\d{12,19}\b", "[redacted]");
    private static bool ContainsAny(string? value, params string[] needles)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
