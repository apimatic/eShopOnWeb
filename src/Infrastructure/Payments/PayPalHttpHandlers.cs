using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal static class PayPalCallContext
{
    private static readonly AsyncLocal<int> WriteCount = new();
    private static readonly AsyncLocal<HttpStatusCode?> LastStatus = new();

    public static void Begin()
    {
        WriteCount.Value = 0;
        LastStatus.Value = null;
    }

    public static void NoteStatus(HttpStatusCode status) => LastStatus.Value = status;

    public static HttpStatusCode? LastStatusCode => LastStatus.Value;

    public static int? LastStatusNumber => LastStatus.Value is HttpStatusCode code ? (int)code : null;

    public static void CountWriteOrThrow()
    {
        var next = WriteCount.Value + 1;
        WriteCount.Value = next;
        if (next > 1)
        {
            throw new PayPalDuplicateSendException();
        }
    }
}

internal sealed class PayPalDuplicateSendException : Exception
{
    public PayPalDuplicateSendException()
        : base("A PayPal write was blocked because the SDK retried a non-idempotent send.")
    {
    }
}

internal sealed class PayPalWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var isToken = path.Contains("/v1/oauth2/token", System.StringComparison.OrdinalIgnoreCase);
        var isWrite = request.Method == HttpMethod.Post
            || request.Method == HttpMethod.Patch
            || request.Method == HttpMethod.Delete
            || request.Method == HttpMethod.Put;

        if (isWrite && !isToken)
        {
            PayPalCallContext.CountWriteOrThrow();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

internal sealed class PayPalStatusCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        PayPalCallContext.NoteStatus(response.StatusCode);
        return response;
    }
}
