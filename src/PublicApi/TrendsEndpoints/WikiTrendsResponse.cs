using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.TrendsEndpoints;

public sealed class WikiTrendsResponse
{
    public int Received { get; init; }
    public IReadOnlyList<WikiEditDto> Matches { get; init; } = [];
    public IReadOnlyList<WikiEditDto> LatestCommons { get; init; } = [];
    public string StoppedBecause { get; init; } = string.Empty;
}
