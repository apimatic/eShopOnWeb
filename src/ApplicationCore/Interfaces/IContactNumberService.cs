using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Outcome of registering a contact number: the created number, or the reasons it was rejected.</summary>
public record ContactNumberRegistration(bool Succeeded, ContactNumber? ContactNumber, IReadOnlyList<string> Errors)
{
    public static ContactNumberRegistration Success(ContactNumber contactNumber) =>
        new(true, contactNumber, System.Array.Empty<string>());

    public static ContactNumberRegistration Rejected(IReadOnlyList<string> errors) =>
        new(false, null, errors);
}

/// <summary>
/// Manages the mobile numbers a shopper has on file. Every operation is scoped to a single shopper:
/// one shopper can never see, use, or delete another's number.
/// </summary>
public interface IContactNumberService
{
    Task<ContactNumberRegistration> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the caller's numbers. Returns false when it does not exist or is not the caller's.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
