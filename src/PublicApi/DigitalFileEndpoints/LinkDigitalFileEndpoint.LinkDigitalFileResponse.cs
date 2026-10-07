using System;

namespace Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;

public class LinkDigitalFileResponse : BaseResponse
{
    public LinkDigitalFileResponse(Guid correlationId) : base(correlationId)
    {
    }

    public LinkDigitalFileResponse()
    {
    }

    public int CatalogItemId { get; set; }
    public string BoxFileId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long? Size { get; set; }
}
