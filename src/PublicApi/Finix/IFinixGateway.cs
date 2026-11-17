using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public interface IFinixGateway
{
    Task<FinixIdentity> CreateIdentityAsync(string buyerId, CancellationToken cancellationToken);

    Task<FinixIdentity?> FindIdentityByTagAsync(string key, string value, CancellationToken cancellationToken);

    Task<FinixInstrument> CreateCardInstrumentAsync(
        string identityId,
        CardInput card,
        string? requestTag,
        CancellationToken cancellationToken);

    Task<FinixInstrument?> GetInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken);

    Task<FinixInstrument?> FindInstrumentByTagAsync(string key, string value, CancellationToken cancellationToken);

    Task DisableInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken);

    Task<FinixAuthorization> AuthorizeAsync(
        long amountCents,
        string source,
        string idempotencyId,
        CancellationToken cancellationToken);

    Task<FinixAuthorization?> FindAuthorizationByIdempotencyAsync(
        string idempotencyId,
        CancellationToken cancellationToken);

    Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixAuthorization> CaptureAsync(
        string authorizationId,
        long amountCents,
        CancellationToken cancellationToken);

    Task<FinixAuthorization> VoidAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixTransfer> RefundAsync(string transferId, int amountCents, CancellationToken cancellationToken);

    Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken);

    Task<FinixPage<FinixTransfer>> ListTransferReversalsAsync(
        string transferId,
        CancellationToken cancellationToken);

    Task<FinixPage<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<FinixPage<FinixAuthorization>> ListAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<FinixFeeSummary> GetFeesAsync(string transferId, CancellationToken cancellationToken);
}
