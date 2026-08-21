using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record TwilioSendMessageRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt = null);
