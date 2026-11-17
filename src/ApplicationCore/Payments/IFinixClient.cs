using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record FinixCardDetails(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string SecurityCode,
    string Name);

public interface IFinixClient
{
    Task<FinixIdentity> CreateBuyerIdentityAsync(string firstName, string lastName, string? email, CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(string identityId, FinixCardDetails card, FinixAddress address, CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrument> DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CreateAuthorizationAsync(int amountCents, string currency, string source, string idempotencyId, IReadOnlyDictionary<string, string>? tags = null, CancellationToken cancellationToken = default);

    Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CaptureAuthorizationAsync(string authorizationId, int captureAmountCents, CancellationToken cancellationToken = default);

    Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken = default);

    Task<FinixTransfer> CreateRefundAsync(string parentTransferId, int refundAmountCents, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<int?> GetUnreversedAmountAsync(string parentTransferId, CancellationToken cancellationToken = default);

    Task<FinixTransfer?> FindTransferByIdempotencyAsync(string idempotencyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixFee>> ListFeesAsync(string linkedToId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
