using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Shopper-scoped management of the mobile numbers a shopper has on file. Every method acts only on
/// numbers owned by <c>ownerId</c>.</summary>
public interface IContactNumberService
{
    /// <summary>Register a number for the shopper. Rejects a number the provider does not consider usable and
    /// stores the provider's canonical form. Throws <see cref="Exceptions.InvalidPhoneNumberException"/> if invalid.</summary>
    Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber, CancellationToken ct);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken ct);

    /// <summary>Remove one of the shopper's numbers. Returns false if not found among the caller's numbers.</summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken ct);
}
