using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Models;
using Twilio.Models;

namespace Twilio.Errors;

public sealed class UpdateParticipantInConversationError : ApiError
{
    private readonly Optional<AccountsCallsRecordingsSidJson201041408Error1> _accountsCallsRecordingsSidJson201041408Error1Value;

    private UpdateParticipantInConversationError(Optional<AccountsCallsRecordingsSidJson201041408Error1> accountsCallsRecordingsSidJson201041408Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _accountsCallsRecordingsSidJson201041408Error1Value = accountsCallsRecordingsSidJson201041408Error1Value;
    }

    private static UpdateParticipantInConversationError AsAccountsCallsRecordingsSidJson201041408Error1(AccountsCallsRecordingsSidJson201041408Error1 value) =>
        new(Optional<AccountsCallsRecordingsSidJson201041408Error1>.Some(value), default);

    private static UpdateParticipantInConversationError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetAccountsCallsRecordingsSidJson201041408Error1(out AccountsCallsRecordingsSidJson201041408Error1 value) =>
        _accountsCallsRecordingsSidJson201041408Error1Value.TryGetValue(out value);

    internal static Task<UpdateParticipantInConversationError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 404 or 429 or 500 or 503 => FromJson<AccountsCallsRecordingsSidJson201041408Error1>(response, ct).As(AsAccountsCallsRecordingsSidJson201041408Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class UpdateParticipantInConversationErrorResponse : IErrorResponse<UpdateParticipantInConversationError>
{
    public static UpdateParticipantInConversationErrorResponse Instance { get; } = new();

    private UpdateParticipantInConversationErrorResponse()
    {
    }

    public Task<UpdateParticipantInConversationError> Map(HttpResponseMessage response, CancellationToken ct) =>
        UpdateParticipantInConversationError.Create(response, ct);
}
