using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Registration, listing and removal of a shopper's mobile contact numbers.</summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validates the number with the provider and, if it is a usable destination, stores its canonical
    /// E.164 form for the shopper. A number the provider does not consider usable is rejected here.
    /// </summary>
    Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the shopper's own numbers. Returns false if it is not theirs or does not exist.</summary>
    Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of registering a contact number.</summary>
public sealed record RegisterContactNumberResult(bool Success, ContactNumber? ContactNumber, string? RejectionReason);
