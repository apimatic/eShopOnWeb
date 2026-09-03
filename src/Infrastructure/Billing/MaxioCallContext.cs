using System.Net;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioCallContext
{
    private static readonly AsyncLocal<HttpStatusCode?> LastStatus = new();

    public static HttpStatusCode? LastHttpStatus
    {
        get => LastStatus.Value;
        set => LastStatus.Value = value;
    }
}
