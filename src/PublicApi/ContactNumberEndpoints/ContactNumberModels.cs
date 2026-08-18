using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Register a mobile number for the signed-in shopper.</summary>
public class RegisterContactNumberRequest
{
    /// <summary>The number as typed (E.164 or national format).</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Optional ISO-3166 alpha-2 country hint, used when the number is in national format.</summary>
    public string? CountryCode { get; set; }
}

/// <summary>One registered contact number.</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>Response to a registration: carries the new id at the top level.</summary>
public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>The caller's registered numbers.</summary>
public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

/// <summary>Remove one of the caller's registered numbers.</summary>
public class DeleteContactNumberRequest
{
    public int ContactNumberId { get; init; }

    public DeleteContactNumberRequest(int contactNumberId) => ContactNumberId = contactNumberId;
}
