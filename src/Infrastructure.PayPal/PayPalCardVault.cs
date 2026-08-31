using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// ICardVault over the PayPal Payment Method Tokens API (v3). Cards are vaulted directly at
/// PayPal; only the vault token and safe display data (brand, last digits, expiry) come back.
/// </summary>
public class PayPalCardVault : ICardVault
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalCardVault> _logger;

    public PayPalCardVault(PayPalServerSdkClient client, ILogger<PayPalCardVault> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task<VaultedCardResult> VaultCardAsync(CardDetails card, string merchantCustomerId, string? payPalCustomerId, string requestKey, CancellationToken ct)
        => Bounded(async token =>
        {
            var body = new PaymentTokenRequest
            {
                Customer = new Customer
                {
                    Id = payPalCustomerId,
                    MerchantCustomerId = merchantCustomerId
                },
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Number = card.Number,
                        Expiry = card.Expiry,
                        SecurityCode = card.SecurityCode,
                        Name = card.Name,
                        BillingAddress = PayPalPaymentGateway.BuildAddress(card.BillingAddress)
                    }
                }
            };

            try
            {
                var response = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: requestKey,
                    body: body,
                    ct: token);

                var cardEntity = response.PaymentSource?.Card;
                return new VaultedCardResult(
                    response.Id ?? throw new PaymentGatewayException("PayPal did not return a vault payment token id."),
                    response.Customer?.Id,
                    cardEntity?.Brand.WireValue(),
                    cardEntity?.LastDigits,
                    cardEntity?.Expiry);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                if (ex.Error.TryGetError1(out var error))
                {
                    throw FromPayPalError(ex.Error, error.Name, error.Message, error.DebugId, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError(raw, ex);
                }
                throw UnknownProviderError("vault the card", ex);
            }
        }, ct);

    public Task DeleteCardAsync(string vaultPaymentTokenId, CancellationToken ct)
        => Bounded(async token =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(
                    id: vaultPaymentTokenId,
                    ct: token);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                if (ex.Error.TryGetError1(out var error))
                {
                    throw FromPayPalError(ex.Error, error.Name, error.Message, error.DebugId, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError(raw, ex);
                }
                throw UnknownProviderError("delete the vaulted card", ex);
            }
        }, ct);

    private PaymentGatewayException FromPayPalError(ApiError apiError, string name, string message, string? debugId, Exception inner)
    {
        var status = PayPalResponseStatusTracker.LastStatus
            ?? (apiError.TryGetRawError(out var raw) ? (int?)raw.StatusCode : null);
        _logger.LogWarning("PayPal vault rejected the request: {Name} {Message} (debug id {DebugId}, HTTP {Status})",
            name, message, debugId, status);
        return new PaymentGatewayException($"PayPal error {name}: {message} (debug id: {debugId})", status, debugId, inner);
    }

    private PaymentGatewayException FromRawError(RawError raw, Exception inner)
    {
        _logger.LogWarning("PayPal vault rejected the request with HTTP {Status}", (int)raw.StatusCode);
        return new PaymentGatewayException($"PayPal rejected the request (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode, null, inner);
    }

    private PaymentGatewayException UnknownProviderError(string operation, Exception inner)
        => new PaymentGatewayException($"PayPal could not {operation}; the failure could not be classified.",
            PayPalResponseStatusTracker.LastStatus, null, inner);

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (PaymentGatewayException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new PaymentGatewayException("The payment provider did not respond within the allowed time.", null, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException("The payment provider could not be reached.", null, null, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PaymentGatewayException("The payment provider returned a response that could not be processed.",
                PayPalResponseStatusTracker.LastStatus, null, ex);
        }
    }

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            await call(cts.Token);
        }
        catch (PaymentGatewayException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new PaymentGatewayException("The payment provider did not respond within the allowed time.", null, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException("The payment provider could not be reached.", null, null, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PaymentGatewayException("The payment provider returned a response that could not be processed.",
                PayPalResponseStatusTracker.LastStatus, null, ex);
        }
    }
}
