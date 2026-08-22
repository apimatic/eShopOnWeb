using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public sealed record PhoneLookupResult(
    bool Valid,
    string? CanonicalNumber,
    IReadOnlyList<string> ValidationErrors);

public sealed record SmsMessageList(
    IReadOnlyList<SmsMessageSnapshot> Messages,
    bool Truncated,
    string FromNumber);
