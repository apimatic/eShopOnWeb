using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.TrendsEndpoints;

public sealed record WikiEditDto(
    string Wiki,
    string PageTitle,
    long RevisionId,
    string? Editor,
    DateTimeOffset Timestamp,
    IReadOnlyList<RevisionSlotDto> Slots);
