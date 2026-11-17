using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin, typed client for the Finix API described by finix-spec/finix-openapi.yaml.
/// </summary>
public interface IFinixClient
{
    Task<FinixIdentity> CreateBuyerIdentityAsync(
        string buyerId,
        string? email,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken);

    Task<FinixPaymentInstrument> CreateCardPaymentInstrumentAsync(
        string identityId,
        CardInput card,
        string buyerId,
        CancellationToken cancellationToken);

    Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken);

    Task<FinixAuthorization> CreateAuthorizationAsync(
        long amountInCents,
        string currency,
        string source,
        string idempotencyId,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken);

    Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId,
        long amountInCents,
        CancellationToken cancellationToken);

    Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken);

    Task<FinixTransfer> CreateReversalAsync(
        string transferId,
        long amountInCents,
        string idempotencyId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
