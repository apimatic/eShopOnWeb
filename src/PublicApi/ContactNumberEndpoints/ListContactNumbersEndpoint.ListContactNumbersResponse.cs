using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListContactNumbersResponse()
    {
    }

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
