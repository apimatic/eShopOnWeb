using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register, in any format the provider can parse.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse
{
    public RegisterContactNumberResponse(int contactNumberId, string phoneNumber, DateTimeOffset createdAt)
    {
        ContactNumberId = contactNumberId;
        PhoneNumber = phoneNumber;
        CreatedAt = createdAt;
    }

    /// <summary>Identifier of the newly registered number (top-level, so the flow can be driven end to end).</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form of the number, as stored.</summary>
    public string PhoneNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class ContactNumberDto
{
    public ContactNumberDto(int contactNumberId, string phoneNumber, DateTimeOffset createdAt)
    {
        ContactNumberId = contactNumberId;
        PhoneNumber = phoneNumber;
        CreatedAt = createdAt;
    }

    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
