using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Body of POST /api/contact-numbers.</summary>
public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to register, in any form the provider can canonicalize.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Owner identity, set from the token by the endpoint — never taken from the request body.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string OwnerId { get; set; } = string.Empty;
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;

    public static ContactNumberDto From(ContactNumber c) => new()
    {
        ContactNumberId = c.Id,
        PhoneNumber = c.PhoneNumber
    };
}

/// <summary>Response of POST /api/contact-numbers — carries the new identifier at the top level.</summary>
public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>Response of GET /api/contact-numbers.</summary>
public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

/// <summary>Owner-scoped request carrying only the caller identity (set from the token).</summary>
public class OwnerScopedRequest : BaseRequest
{
    public string OwnerId { get; set; } = string.Empty;
}

/// <summary>Request for DELETE /api/contact-numbers/{contactNumberId}.</summary>
public class DeleteContactNumberRequest : BaseRequest
{
    public string OwnerId { get; set; } = string.Empty;
    public int ContactNumberId { get; set; }
}
