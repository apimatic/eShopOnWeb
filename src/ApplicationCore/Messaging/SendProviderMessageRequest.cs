using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record SendProviderMessageRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt = null);
