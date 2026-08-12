using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Result of trying to register a mobile number.</summary>
public record ContactNumberRegistrationResult(bool Succeeded, int ContactNumberId, string? Error);

/// <summary>Manages the mobile numbers a shopper has on file. All operations are scoped to the owner.</summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validates the number with the provider and, if it is a usable destination, stores its canonical
    /// form for the shopper. A number the provider does not consider usable is rejected here.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the caller's numbers. Returns false if it is not theirs or does not exist.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
