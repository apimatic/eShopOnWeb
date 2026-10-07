using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;

public class ListDigitalFilesResponse : BaseResponse
{
    public ListDigitalFilesResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListDigitalFilesResponse()
    {
    }

    public List<DigitalFileDto> DigitalFiles { get; set; } = new();

    /// <summary>False when the folder could not be listed in full; <see cref="DigitalFiles"/> is then partial.</summary>
    public bool Complete { get; set; }

    /// <summary>Folder entries whose details could not be read; they cannot be linked.</summary>
    public List<UnreadableDigitalFileDto> UnreadableEntries { get; set; } = new();
}
