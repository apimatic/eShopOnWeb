using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;

public class LinkDigitalFileRequest : BaseRequest
{
    /// <summary>Taken from the route.</summary>
    [JsonIgnore]
    public int CatalogItemId { get; set; }

    /// <summary>The Box id of a file listed by GET api/digital-files.</summary>
    [Required]
    public string FileId { get; set; } = string.Empty;

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}
