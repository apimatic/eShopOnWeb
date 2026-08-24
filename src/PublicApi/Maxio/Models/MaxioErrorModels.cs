using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

// Mirrors components/schemas/errors/Error-List-Response.yaml
public class MaxioErrorListResponse
{
    public List<string>? Errors { get; set; }
}
