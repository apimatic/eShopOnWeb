using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

internal class ErrorsEnvelope
{
    public List<string>? Errors { get; set; }
}
