using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateContactNumberResponse()
    {
    }

    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;

    public static CreateContactNumberResponse From(ShopperContactNumber contactNumber, Guid correlationId)
    {
        return new CreateContactNumberResponse(correlationId)
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.CanonicalNumber
        };
    }
}
