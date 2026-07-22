using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ValidateCatalogResponse : BaseResponse
{
    public ValidateCatalogResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ValidateCatalogResponse()
    {
    }

    public bool IsValid { get; set; }
    public string ProductFamilyHandle { get; set; }
    public int? ProductFamilyId { get; set; }
    public bool IsMeteredComponentValid { get; set; }
    public int? MeteredComponentId { get; set; }
    public string MeteredComponentKind { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
