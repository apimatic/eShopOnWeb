namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>Why a contact-number registration was rejected, if it was.</summary>
public enum RegisterContactNumberError
{
    None = 0,

    /// <summary>The number is missing/empty.</summary>
    Missing = 1,

    /// <summary>The provider does not consider the number a usable destination.</summary>
    NotAUsableDestination = 2
}

/// <summary>Outcome of registering a contact number.</summary>
public record RegisterContactNumberResult(int? ContactNumberId, string? CanonicalNumber, RegisterContactNumberError Error)
{
    public bool Succeeded => Error == RegisterContactNumberError.None && ContactNumberId.HasValue;

    public static RegisterContactNumberResult Success(int contactNumberId, string canonicalNumber) =>
        new(contactNumberId, canonicalNumber, RegisterContactNumberError.None);

    public static RegisterContactNumberResult Failure(RegisterContactNumberError error) =>
        new(null, null, error);
}
