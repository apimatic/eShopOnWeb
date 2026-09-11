using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class SubscriptionPlanService : IMaxioSubscriptionPlanService
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly MaxioSettings _settings;

    public SubscriptionPlanService(IMaxioApiClient maxioClient, IOptions<MaxioSettings> settings)
    {
        _maxioClient = maxioClient;
        _settings = settings.Value;
    }

    public Task<List<PlanDto>> GetPlansAsync()
    {
        return _maxioClient.GetPlansAsync(_settings.ProductFamilyHandle);
    }
}
