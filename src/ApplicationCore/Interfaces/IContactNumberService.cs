using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Registration of shopper contact numbers. A number the provider does not consider a usable
/// destination is rejected here, at registration time, rather than when a later message fails.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validates <paramref name="rawNumber"/> with the provider and, if usable, stores the
    /// provider's canonical form on file for <paramref name="buyerId"/>. If the same canonical
    /// number is already on file for the shopper, the existing record is returned.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a registration attempt. <see cref="RejectionReason"/> is set only on rejection.</summary>
public record ContactNumberRegistrationResult(bool Succeeded, ContactNumber? ContactNumber, string? RejectionReason)
{
    public static ContactNumberRegistrationResult Registered(ContactNumber contactNumber) => new(true, contactNumber, null);
    public static ContactNumberRegistrationResult Rejected(string reason) => new(false, null, reason);
}
