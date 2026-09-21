using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Body of <c>POST /api/contact-numbers</c>: the caller-typed mobile number to register.</summary>
public class RegisterContactNumberRequest
{
    public string Number { get; set; } = string.Empty;
}

/// <summary>Response of <c>POST /api/contact-numbers</c>. Returns the new number's identifier.</summary>
public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form actually stored.</summary>
    public string Number { get; set; } = string.Empty;
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
