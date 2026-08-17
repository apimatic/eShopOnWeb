using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>Registers a mobile number for the signed-in shopper.</summary>
public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register, in any format the provider can canonicalize (e.g. E.164).</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>A registered contact number. <c>contactNumberId</c> is the identifier operations act on.</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical (E.164) form of the number.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>Response to registering a contact number; carries the new id at the top level.</summary>
public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>The caller's registered contact numbers.</summary>
public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
