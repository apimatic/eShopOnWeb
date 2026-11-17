using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IFinixClient
{
    Task<FinixIdentityResult> CreateBuyerIdentityAsync(
        string firstName,
        string lastName,
        string? email,
        string? phone,
        FinixAddress? address,
        CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrumentResult> CreatePaymentInstrumentAsync(
        string identityId,
        FinixCard card,
        CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrumentResult> GetPaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken = default);

    Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResult> CreateAuthorizationAsync(
        int amountCents,
        string currency,
        string sourcePaymentInstrumentId,
        string idempotencyId,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResult?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResult> CaptureAuthorizationAsync(
        string authorizationId,
        int amountCents,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResult> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixTransferResult> GetTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default);

    Task<FinixTransferResult> CreateReversalAsync(
        string transferId,
        int amountCents,
        string idempotencyId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixTransferResult>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
