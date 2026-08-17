using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Registers, lists and removes a shopper's on-file mobile contact numbers.</summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validates the raw number with the provider and, if usable, stores the provider's canonical form
    /// for the shopper. A number the provider does not consider a usable destination is rejected here.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Removes a number owned by the shopper. Returns false if it does not exist or is not theirs.</summary>
    Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);
}

/// <summary>Outcome of registering a contact number.</summary>
public record ContactNumberRegistrationResult(bool Succeeded, ContactNumber? ContactNumber, string? Error)
{
    public static ContactNumberRegistrationResult Ok(ContactNumber contactNumber) => new(true, contactNumber, null);
    public static ContactNumberRegistrationResult Rejected(string error) => new(false, null, error);
}
