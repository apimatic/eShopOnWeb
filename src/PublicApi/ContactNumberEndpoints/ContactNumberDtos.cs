using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Body for registering a contact number. <see cref="PhoneNumber"/> is whatever the caller
/// typed; the stored value is the provider's canonical form. <see cref="CountryCode"/> is an
/// optional ISO 3166-1 alpha-2 hint for numbers given in national format.</summary>
public record RegisterContactNumberRequest(string PhoneNumber, string? CountryCode);

// Commands carry the caller identity (extracted from the JWT in the route delegate) so each
// endpoint's HandleAsync matches the IEndpoint contract.
public record RegisterContactNumberCommand(string OwnerId, string PhoneNumber, string? CountryCode);
public record ListContactNumbersCommand(string OwnerId);
public record DeleteContactNumberCommand(string OwnerId, int ContactNumberId);

public record ContactNumberDto(int ContactNumberId, string PhoneNumber, string? NationalFormat, string? CountryCode, DateTimeOffset CreatedDate)
{
    public static ContactNumberDto From(ContactNumber c) =>
        new(c.Id, c.PhoneNumber, c.NationalFormat, c.CountryCode, c.CreatedDate);
}

public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public ContactNumberDto? ContactNumber { get; set; }
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
