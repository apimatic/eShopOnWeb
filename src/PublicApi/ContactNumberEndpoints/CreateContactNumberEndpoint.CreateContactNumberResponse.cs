using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public CreateContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Error { get; set; }
    public IReadOnlyList<string> ValidationErrors { get; set; } = Array.Empty<string>();
}
