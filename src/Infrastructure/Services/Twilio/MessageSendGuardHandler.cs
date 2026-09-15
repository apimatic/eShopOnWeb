using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Refuses a transport-level re-send of a message-creating POST that this integration did not authorise.
/// The SDK's retry pipeline resends an <see cref="HttpRequestException"/> on ANY verb, so a create-message POST
/// whose socket drops after the bytes reached the provider could otherwise become a duplicate customer text.
/// The refusal throws a private sentinel (NOT an <see cref="HttpRequestException"/>), so the pipeline propagates
/// it rather than treating the refusal itself as retryable. Only active inside a <see cref="SendGuardScope"/>
/// (send/schedule); status reads, cancels and redacts pass through untouched.
/// </summary>
public sealed class MessageSendGuardHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (SendGuardScope.IsActive && request.Method == HttpMethod.Post && SendGuardScope.NextAttempt() > 1)
        {
            throw new DuplicateSendBlockedException();
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Private sentinel — deliberately not an <see cref="HttpRequestException"/> so the SDK retry pipeline does not retry it.</summary>
internal sealed class DuplicateSendBlockedException : Exception
{
    public DuplicateSendBlockedException()
        : base("A transport retry of a message send was refused to avoid sending the message more than once.")
    {
    }
}
