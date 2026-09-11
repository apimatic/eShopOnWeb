using System;

namespace Microsoft.eShopWeb.PublicApi;

public class EmptyRequest : BaseRequest
{
    public EmptyRequest()
    {
        _correlationId = Guid.NewGuid();
    }
}
