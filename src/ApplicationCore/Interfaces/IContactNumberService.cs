using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's on-file mobile contact numbers. Every operation is scoped to the owning
/// shopper: one shopper can never see, use, or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validate a raw number with the provider and, if usable, store its canonical E.164 form for the
    /// shopper. A number the provider does not consider a usable destination is rejected here (not at
    /// send time). Throws <see cref="Exceptions.SmsGatewayException"/> if the provider is unavailable.
    /// </summary>
    Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken ct = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Remove one of the shopper's numbers. Returns false if it is not the shopper's / does not exist.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default);
}

/// <summary>Outcome of registering a contact number.</summary>
public sealed record RegisterContactNumberResult(bool Success, ContactNumber? ContactNumber, string? RejectionReason)
{
    public static RegisterContactNumberResult Registered(ContactNumber number) => new(true, number, null);
    public static RegisterContactNumberResult Rejected(string reason) => new(false, null, reason);
}
