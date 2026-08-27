using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    /// <summary>
    /// Validates a number with the provider and, if usable, stores the provider's canonical
    /// form for the buyer. Invalid numbers are rejected here, not at send time.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a number owned by the buyer. Returns false when not found or not owned.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}

public class ContactNumberRegistrationResult
{
    public bool Succeeded { get; set; }
    public ContactNumber? ContactNumber { get; set; }
    public string? Error { get; set; }

    public static ContactNumberRegistrationResult Success(ContactNumber contactNumber) =>
        new() { Succeeded = true, ContactNumber = contactNumber };

    public static ContactNumberRegistrationResult Failure(string error) =>
        new() { Succeeded = false, Error = error };
}
