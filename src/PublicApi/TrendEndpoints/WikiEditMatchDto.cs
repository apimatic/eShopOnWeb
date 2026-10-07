using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

/// <summary>
/// An edit whose page title mentions a catalog brand or type.
/// </summary>
public class WikiEditMatchDto : WikiEditDto
{
    /// <summary>
    /// The catalog brands/types the title mentions, in catalog spelling.
    /// </summary>
    public List<string> MatchedTerms { get; set; } = new List<string>();
}
