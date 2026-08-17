using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// Outcome of registering a contact number. When the provider does not consider the number a
/// usable destination the registration is rejected here (rather than later when a message fails).
/// </summary>
public class RegisterContactNumberResult
{
    private RegisterContactNumberResult() { }

    public bool Succeeded { get; private init; }
    public ContactNumber? ContactNumber { get; private init; }
    public IReadOnlyList<string> ValidationErrors { get; private init; } = new List<string>();

    public static RegisterContactNumberResult Success(ContactNumber contactNumber) =>
        new() { Succeeded = true, ContactNumber = contactNumber };

    public static RegisterContactNumberResult Rejected(IReadOnlyList<string> validationErrors) =>
        new() { Succeeded = false, ValidationErrors = validationErrors };
}
