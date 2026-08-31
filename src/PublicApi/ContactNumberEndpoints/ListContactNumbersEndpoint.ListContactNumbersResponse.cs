using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(Guid correlationId) : base(correlationId) { }
    public ListContactNumbersResponse() { }

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
