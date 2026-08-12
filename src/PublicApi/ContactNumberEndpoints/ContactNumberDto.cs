using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>A contact number as returned to its owner. The canonical E.164 number is shown to the owner only.</summary>
public record ContactNumberDto(int ContactNumberId, string PhoneNumber, DateTimeOffset RegisteredAt)
{
    public static ContactNumberDto From(ContactNumber contactNumber) =>
        new(contactNumber.Id, contactNumber.E164Number, contactNumber.RegisteredAt);
}
