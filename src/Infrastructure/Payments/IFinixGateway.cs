using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public record FinixIdentity(string Id);

public record FinixCard(string Id,
    string? Brand,
    string? Last4,
    int? ExpirationMonth,
    int? ExpirationYear,
    string? Name,
    string IdentityId,
    bool? Enabled);

public record FinixAuthorization(string Id,
    string? State,
    long? Amount,
    string? TransferId,
    bool? IsVoid,
    DateTimeOffset? ExpiresAt,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset? CreatedAt,
    string? Merchant);

public record FinixTransfer(string Id,
    string? Type,
    string? State,
    long? Amount,
    long? Fee,
    string? ParentTransferId,
    DateTimeOffset? CreatedAt,
    string? FailureCode,
    string? FailureMessage,
    string? Merchant);

public record FinixTransferListing(IReadOnlyList<FinixTransfer> Transfers, int PagesFetched);

public record FinixAuthorizationListing(IReadOnlyList<FinixAuthorization> Authorizations, int PagesFetched);

public interface IFinixGateway
{
    Task<FinixIdentity> CreateBuyerIdentityAsync(string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken);

    Task<FinixCard> CreateCardAsync(string identityId, CardData card, CancellationToken cancellationToken);

    Task DisableCardAsync(string paymentInstrumentId, CancellationToken cancellationToken);

    Task<FinixAuthorization> AuthorizeAsync(string merchantId,
        string currency,
        long amountCents,
        string source,
        string idempotencyId,
        string? identityId,
        CancellationToken cancellationToken);

    Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixAuthorization> CaptureAsync(string authorizationId, long amountCents, CancellationToken cancellationToken);

    Task<FinixAuthorization> VoidAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken);

    Task<FinixTransfer> RefundAsync(string transferId, long amountCents, CancellationToken cancellationToken);

    Task<FinixTransferListing> ListAllTransfersAsync(string merchantId, CancellationToken cancellationToken);

    Task<FinixAuthorizationListing> ListAllAuthorizationsAsync(string merchantId, CancellationToken cancellationToken);
}
