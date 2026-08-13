using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }

    public static ContactNumberDto From(ContactNumber c) => new()
    {
        ContactNumberId = c.Id,
        PhoneNumber = c.PhoneNumber,
        CreatedDate = c.CreatedDate,
    };
}

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to register, in any format the provider can canonicalise.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Set from the authenticated caller; never bound from the request body.</summary>
    [JsonIgnore]
    public string? CallerId { get; set; }
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public ContactNumberDto? ContactNumber { get; set; }
}

public class ListContactNumbersRequest : BaseRequest
{
    [JsonIgnore]
    public string? CallerId { get; set; }
}

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(Guid correlationId) : base(correlationId) { }
    public ListContactNumbersResponse() { }

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class DeleteContactNumberRequest : BaseRequest
{
    public int ContactNumberId { get; set; }

    [JsonIgnore]
    public string? CallerId { get; set; }
}

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public DeleteContactNumberResponse() { }
}
