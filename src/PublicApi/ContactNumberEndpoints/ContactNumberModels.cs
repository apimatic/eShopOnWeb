using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to put on file. Whatever the caller types; the provider's canonical form is stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Set from the token by the endpoint; not part of the request body.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

/// <summary>Carries the caller identity for listing the caller's own numbers.</summary>
public class ListContactNumbersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

/// <summary>Carries the caller identity and the number to remove.</summary>
public class DeleteContactNumberRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public int ContactNumberId { get; set; }
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    /// <summary>Identifier of the created contact number (top-level, so the flow can be driven end to end).</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider-canonical form of the number that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ContactNumberValidationResponse : BaseResponse
{
    public ContactNumberValidationResponse(Guid correlationId) : base(correlationId) { }
    public ContactNumberValidationResponse() { }

    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }
}

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(Guid correlationId) : base(correlationId) { }
    public ListContactNumbersResponse() { }

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
