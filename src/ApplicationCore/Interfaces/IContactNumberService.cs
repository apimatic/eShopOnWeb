using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    /// <summary>
    /// Validates a raw number with the provider and, if usable, stores the
    /// provider's canonical form for the shopper.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber, string? countryCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a number owned by the shopper and calls off any provider-held
    /// scheduled messages to it. False when the number is not the caller's.
    /// </summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}

public record ContactNumberRegistrationResult(ContactNumber? ContactNumber, IReadOnlyList<string> Errors)
{
    public bool Success => ContactNumber is not null;

    public static ContactNumberRegistrationResult Failed(params string[] errors)
        => new(null, errors);
}
