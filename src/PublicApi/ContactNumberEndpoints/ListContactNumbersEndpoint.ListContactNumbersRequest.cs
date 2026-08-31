using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersRequest : BaseRequest
{
}

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
