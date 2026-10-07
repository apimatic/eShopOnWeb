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

    /// <summary>The Box folder the files were listed from.</summary>
    public string FolderName { get; set; } = string.Empty;

    public List<DigitalFileDto> Files { get; set; } = new();

    /// <summary>True when the folder holds more files than were listed.</summary>
    public bool IsTruncated { get; set; }
}
