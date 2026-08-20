using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
}

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public CreateContactNumberResponse() { }

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
