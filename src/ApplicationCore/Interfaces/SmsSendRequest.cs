using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class SmsSendRequest
{
    public required string To { get; init; }
    public required string Body { get; init; }
    public DateTimeOffset? SendAt { get; init; }
}
