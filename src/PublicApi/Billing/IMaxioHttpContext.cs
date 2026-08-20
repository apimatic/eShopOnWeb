using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public interface IMaxioResponseContext
{
    HttpStatusCode? LastStatusCode { get; }

    IDisposable BeginScope();

    void Record(HttpStatusCode statusCode);
}

public interface IMaxioWriteGuard
{
    IDisposable BeginScope();

    bool TryMarkPost();
}
