using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference.Models;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record FinixCardInput(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string SecurityCode,
    string Name,
    FinixAddressInput? Address);

public sealed record FinixAddressInput(
    string? Line1,
    string? Line2,
    string? City,
    string? Region,
    string? PostalCode,
    string? Country);

public interface IFinixGateway
{
    Task<FinixApiReference.Models.Identity> CreateBuyerIdentityAsync(string buyerId, CancellationToken cancellationToken);
    Task<PaymentInstrumentCard> CreatePaymentInstrumentAsync(string identityId, FinixCardInput card, CancellationToken cancellationToken);
    Task<Authorization> CreateAuthorizationAsync(long amountCents, string source, string idempotencyId, int orderId, string? securityCode, CancellationToken cancellationToken);
    Task<Authorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Authorization>> FindAuthorizationsByIdempotencyIdAsync(string idempotencyId, CancellationToken cancellationToken);
    Task<Authorization> CaptureAuthorizationAsync(string authorizationId, long amountCents, CancellationToken cancellationToken);
    Task<Authorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<Transfer> GetTransferAsync(string transferId, CancellationToken cancellationToken);
    Task<Transfer> CreateRefundAsync(string transferId, long amountCents, string idempotencyKey, CancellationToken cancellationToken);
    Task<Transfer?> FindReversalByIdempotencyIdAsync(string transferId, string idempotencyKey, CancellationToken cancellationToken);
    Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Transfer>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
    Task<IReadOnlyList<Authorization>> ListAuthorizationsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
