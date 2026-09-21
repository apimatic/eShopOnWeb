using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Models;
using TwilioSdk.Models;

namespace TwilioSdk.Errors;

public sealed class FetchParticipant2Error : ApiError
{
    private readonly Optional<AccountsCallsRecordingsSidJson201041408Error1> _accountsCallsRecordingsSidJson201041408Error1Value;

    private FetchParticipant2Error(Optional<AccountsCallsRecordingsSidJson201041408Error1> accountsCallsRecordingsSidJson201041408Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _accountsCallsRecordingsSidJson201041408Error1Value = accountsCallsRecordingsSidJson201041408Error1Value;
    }

    private static FetchParticipant2Error AsAccountsCallsRecordingsSidJson201041408Error1(AccountsCallsRecordingsSidJson201041408Error1 value) =>
        new(Optional<AccountsCallsRecordingsSidJson201041408Error1>.Some(value), default);

    private static FetchParticipant2Error AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetAccountsCallsRecordingsSidJson201041408Error1(out AccountsCallsRecordingsSidJson201041408Error1 value) =>
        _accountsCallsRecordingsSidJson201041408Error1Value.TryGetValue(out value);

    internal static Task<FetchParticipant2Error> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 404 or 429 or 500 or 503 => FromJson<AccountsCallsRecordingsSidJson201041408Error1>(response, ct).As(AsAccountsCallsRecordingsSidJson201041408Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class FetchParticipant2ErrorResponse : IErrorResponse<FetchParticipant2Error>
{
    public static FetchParticipant2ErrorResponse Instance { get; } = new();

    private FetchParticipant2ErrorResponse()
    {
    }

    public Task<FetchParticipant2Error> Map(HttpResponseMessage response, CancellationToken ct) =>
        FetchParticipant2Error.Create(response, ct);
}
