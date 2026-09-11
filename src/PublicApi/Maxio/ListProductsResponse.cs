using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class ListProductsResponse
{
    public List<ProductResponse> products { get; set; } = new();
}
