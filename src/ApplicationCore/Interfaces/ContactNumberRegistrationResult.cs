using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Outcome of registering a contact number. A number the provider does not consider a usable
/// destination is rejected here (<see cref="Succeeded"/> == false) rather than at send time.
/// </summary>
public record ContactNumberRegistrationResult(bool Succeeded, ContactNumber? ContactNumber, string? Error)
{
    public static ContactNumberRegistrationResult Success(ContactNumber contactNumber) =>
        new(true, contactNumber, null);

    public static ContactNumberRegistrationResult Rejected(string error) =>
        new(false, null, error);
}
