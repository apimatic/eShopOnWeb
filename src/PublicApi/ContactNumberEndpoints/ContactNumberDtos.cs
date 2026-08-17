using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number as typed; it is validated and canonicalised before being stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Optional ISO country hint used when the number is in national format.</summary>
    public string? CountryCode { get; set; }
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? NationalFormat { get; set; }
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? NationalFormat { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
}

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class DeleteContactNumberRequest : BaseRequest
{
    public DeleteContactNumberRequest(int contactNumberId)
    {
        ContactNumberId = contactNumberId;
    }

    public int ContactNumberId { get; set; }
}
