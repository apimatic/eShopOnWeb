using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    /// <summary>
    /// Registers a mobile number for a shopper. The number is validated with the provider first;
    /// an unusable number is rejected here, and what gets stored is the provider's canonical form.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken ct = default);

    /// <summary>The caller's own registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Removes one of the caller's numbers. Returns false if it is not theirs / not found.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default);
}
