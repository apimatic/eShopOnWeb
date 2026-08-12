using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Register a mobile number for the signed-in shopper.</summary>
public class RegisterContactNumberRequest
{
    /// <summary>The number as typed by the caller; it is validated and canonicalized by the provider.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>Response for a successful registration. Returns the new id as a top-level field.</summary>
public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form that was stored (not whatever the caller typed).</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>A registered number in a listing.</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}
