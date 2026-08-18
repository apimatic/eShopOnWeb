using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register. Any format the provider can canonicalize is accepted.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string E164Number { get; set; } = string.Empty;
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string E164Number { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
