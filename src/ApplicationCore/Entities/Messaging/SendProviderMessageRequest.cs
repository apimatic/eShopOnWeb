using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Messaging;

public record SendProviderMessageRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt = null);
