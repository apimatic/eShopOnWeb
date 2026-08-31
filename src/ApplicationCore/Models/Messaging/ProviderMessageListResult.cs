using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

public class ProviderMessageListResult
{
    public ProviderMessageListResult(IReadOnlyList<ProviderTextMessage> messages, bool truncated)
    {
        Messages = messages;
        Truncated = truncated;
    }

    public IReadOnlyList<ProviderTextMessage> Messages { get; }

    /// <summary>True when the provider had more pages than were walked.</summary>
    public bool Truncated { get; }
}
