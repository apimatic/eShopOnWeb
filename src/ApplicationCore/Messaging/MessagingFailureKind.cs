namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>How a messaging call failed. <see cref="None"/> means it succeeded.</summary>
public enum MessagingFailureKind
{
    None = 0,

    /// <summary>The provider answered with a non-success status (a deterministic rejection).</summary>
    Rejected = 1,

    /// <summary>The provider could not be reached (transport failure, timeout).</summary>
    Unreachable = 2,

    /// <summary>The provider answered but the response could not be processed.</summary>
    UnprocessableResponse = 3
}
