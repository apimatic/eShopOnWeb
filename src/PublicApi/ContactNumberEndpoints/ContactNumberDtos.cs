using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}

public class RegisterContactNumberRequest : AuthenticatedRequest
{
    /// <summary>The mobile number to register, in any form the provider can canonicalise.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    /// <summary>Identifier of the created contact number (top-level, so the flow can be driven end to end).</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical form of the number that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberRejectedResponse : BaseResponse
{
    public RegisterContactNumberRejectedResponse(Guid correlationId) : base(correlationId) { }

    public string Message { get; set; } = "The number is not a usable destination and was not registered.";
    public IReadOnlyList<string> ValidationErrors { get; set; } = new List<string>();
}

public class ListContactNumbersRequest : AuthenticatedRequest
{
}

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(Guid correlationId) : base(correlationId) { }
    public ListContactNumbersResponse() { }

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class DeleteContactNumberRequest : AuthenticatedRequest
{
    public DeleteContactNumberRequest(int contactNumberId)
    {
        ContactNumberId = contactNumberId;
    }

    public int ContactNumberId { get; }
}

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public DeleteContactNumberResponse() { }
}
