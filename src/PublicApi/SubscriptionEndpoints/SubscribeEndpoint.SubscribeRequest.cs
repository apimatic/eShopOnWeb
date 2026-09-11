using System;

namespace Microsoft.eShopWeb.PublicApi;

public class SubscribeRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}
