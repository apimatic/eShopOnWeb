using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Registration and management of a shopper's mobile contact numbers. Everything here is scoped to
/// the owning shopper: one shopper can never read, use or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for the shopper. The number is validated and canonicalised against the
    /// provider first; an un-sendable number is rejected here rather than at send time. What is
    /// stored is the provider's canonical (E.164) form.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber);

    /// <summary>The shopper's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId);

    /// <summary>
    /// Removes one of the shopper's numbers. Returns false if no such number belongs to the shopper.
    /// After removal it no longer appears among the shopper's numbers and nothing is sent to it again.
    /// </summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId);
}

/// <summary>Outcome of a registration attempt.</summary>
public class ContactNumberRegistrationResult
{
    public bool Succeeded { get; init; }
    public ContactNumber? ContactNumber { get; init; }
    public string? Error { get; init; }

    public static ContactNumberRegistrationResult Success(ContactNumber number) =>
        new() { Succeeded = true, ContactNumber = number };

    public static ContactNumberRegistrationResult Rejected(string error) =>
        new() { Succeeded = false, Error = error };
}
