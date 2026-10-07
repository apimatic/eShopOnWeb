namespace Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;

public class LinkDigitalFileRequest : BaseRequest
{
    /// <summary>The Box id of a file listed by <c>GET /api/digital-files</c>.</summary>
    public string? BoxFileId { get; set; }
}
