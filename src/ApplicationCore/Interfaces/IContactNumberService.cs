using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Shopper-scoped management of the mobile numbers a shopper has on file. All operations act only
/// on the numbers owned by <c>buyerId</c>.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validate a number with the provider and, if usable, store its canonical E.164 form for the shopper.
    /// Throws <see cref="Exceptions.InvalidPhoneNumberException"/> if the provider rejects the number.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct = default);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Remove one of the caller's numbers. Returns false if it does not exist or is not the caller's.</summary>
    Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken ct = default);
}
