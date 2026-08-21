using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class DuplicateSendRefusedException : PaymentException
{
    public DuplicateSendRefusedException()
        : base(409, "The payment request may already have reached PayPal. Retry using the same order; the original hold will be reused.")
    {
    }
}

/// <summary>
/// Blocks SDK transport retries from sending a write twice. Token fetches are excluded.
/// The "already sent" flag lives in AsyncLocal so it survives a new HttpRequestMessage per attempt.
/// </summary>
public sealed class SingleSendHandler : DelegatingHandler
{
    private static readonly AsyncLocal<bool> WriteScope = new();
    private static readonly AsyncLocal<bool> AlreadySent = new();

    public static IDisposable BeginWrite()
    {
        WriteScope.Value = true;
        AlreadySent.Value = false;
        return new ResetScope();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isWrite = request.Method != HttpMethod.Get
                      && request.Method != HttpMethod.Head
                      && request.Method != HttpMethod.Options
                      && !IsTokenRequest(request);

        if (WriteScope.Value && isWrite)
        {
            if (AlreadySent.Value)
            {
                throw new DuplicateSendRefusedException();
            }

            AlreadySent.Value = true;
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsTokenRequest(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        return path.Contains("/v1/oauth2/token", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ResetScope : IDisposable
    {
        public void Dispose()
        {
            WriteScope.Value = false;
            AlreadySent.Value = false;
        }
    }
}

public sealed class LastStatusHandler : DelegatingHandler
{
    private static readonly AsyncLocal<int?> Status = new();

    public static int? LastStatus => Status.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        Status.Value = (int)response.StatusCode;
        return response;
    }
}
