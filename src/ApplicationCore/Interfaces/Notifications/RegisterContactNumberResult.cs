namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Notifications;

/// <summary>
/// Outcome of registering a contact number. A number the provider does not consider a usable
/// destination is rejected here (<see cref="Succeeded"/> false) rather than at send time.
/// </summary>
public class RegisterContactNumberResult
{
    private RegisterContactNumberResult() { }

    public bool Succeeded { get; private init; }

    /// <summary>Id of the stored number when registration succeeded.</summary>
    public int ContactNumberId { get; private init; }

    /// <summary>The provider-canonical E.164 form that was stored, when registration succeeded.</summary>
    public string? CanonicalNumber { get; private init; }

    /// <summary>Why the number was rejected, when it was.</summary>
    public string? Error { get; private init; }

    public static RegisterContactNumberResult Registered(int id, string canonicalNumber) => new()
    {
        Succeeded = true,
        ContactNumberId = id,
        CanonicalNumber = canonicalNumber
    };

    public static RegisterContactNumberResult Rejected(string error) => new()
    {
        Succeeded = false,
        Error = error
    };
}
