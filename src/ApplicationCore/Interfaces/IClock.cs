using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
