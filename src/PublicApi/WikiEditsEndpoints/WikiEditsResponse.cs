using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.WikiEditsEndpoints;

public class WikiEditsResponse : BaseResponse
{
    public WikiEditsResponse() { }
    public WikiEditsResponse(System.Guid correlationId) : base(correlationId) { }

    public int Received { get; set; }
    public List<WikiEditDto> Matches { get; set; } = new();
    public List<WikiEditDto> LatestCommons { get; set; } = new();
    public string StoppedBecause { get; set; } = string.Empty;
}
