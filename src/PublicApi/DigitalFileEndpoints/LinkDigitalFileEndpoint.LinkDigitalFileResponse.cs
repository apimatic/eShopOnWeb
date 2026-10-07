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

    /// <summary>The linked file's Box id.</summary>
    public string FileId { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    /// <summary>Size in bytes, when Box reported one.</summary>
    public long? Size { get; set; }
}
