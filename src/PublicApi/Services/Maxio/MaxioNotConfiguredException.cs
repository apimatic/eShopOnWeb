using System;

namespace Microsoft.eShopWeb.PublicApi.Services.Maxio;

public class MaxioNotConfiguredException : InvalidOperationException
{
    public MaxioNotConfiguredException(string message) : base(message)
    {
    }
}
