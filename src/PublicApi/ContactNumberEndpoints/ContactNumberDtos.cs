using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Request body for registering a mobile number.</summary>
public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register, in any form the provider can canonicalize (E.164 recommended).</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>A registered contact number.</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredDate { get; set; }
}
