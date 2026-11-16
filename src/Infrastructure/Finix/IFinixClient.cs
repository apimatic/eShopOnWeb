using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public interface IFinixClient
{
    string Currency { get; }
    string MerchantId { get; }

    Task<FinixIdentity> CreateBuyerIdentityAsync(string email, string? firstName, string? lastName,
        FinixAddress? address, CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(FinixCardDetails card, string identityId,
        CancellationToken cancellationToken = default);

    Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CreateAuthorizationAsync(int amountCents, string source, string idempotencyId,
        IReadOnlyDictionary<string, string>? tags, string? securityCode = null,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CaptureAuthorizationAsync(string authorizationId, int captureAmountCents,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken = default);

    Task<FinixTransfer> CreateReversalAsync(string transferId, int refundAmountCents,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
