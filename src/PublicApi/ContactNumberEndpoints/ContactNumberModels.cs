using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register, in E.164 or national format.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse
{
    /// <summary>Identifier of the registered number (top-level, so the flow can be driven onward).</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
