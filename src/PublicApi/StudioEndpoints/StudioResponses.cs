using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Studio;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

public class ListStudioClassesResponse : BaseResponse
{
    public List<StudioClassDto> Classes { get; set; } = new();
}

public class ListStudioSlotsResponse : BaseResponse
{
    public int SessionTypeId { get; set; }
    public List<StudioSlotDto> Slots { get; set; } = new();
}

public class ListStudioTrainersResponse : BaseResponse
{
    public List<StudioTrainerDto> Trainers { get; set; } = new();
}
