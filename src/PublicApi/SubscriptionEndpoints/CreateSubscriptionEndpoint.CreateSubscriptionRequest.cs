using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    [Required]
    public string ProductHandle { get; set; } = string.Empty;

    public class Response : BaseResponse
    {
        public Response(Guid correlationId) : base(correlationId)
        {
        }

        public Response()
        {
        }

        public ApplicationCore.Interfaces.SubscriptionResultDto? Subscription { get; set; }
    }
}
