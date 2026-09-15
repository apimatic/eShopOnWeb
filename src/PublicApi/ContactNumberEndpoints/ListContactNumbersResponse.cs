using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;

    public static ContactNumberDto From(ShopperContactNumber contactNumber)
    {
        return new ContactNumberDto
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.CanonicalNumber
        };
    }
}
