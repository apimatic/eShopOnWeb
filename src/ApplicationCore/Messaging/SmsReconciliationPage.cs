using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public sealed class SmsReconciliationPage
{
    public string FromNumber { get; init; } = string.Empty;
    public IReadOnlyList<SmsMessageResult> Messages { get; init; } = [];
    public bool Truncated { get; init; }
}
