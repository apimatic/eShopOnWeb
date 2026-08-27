using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberRequest : BaseRequest
{
    [FromRoute(Name = "contactNumberId")]
    public int ContactNumberId { get; set; }
}
