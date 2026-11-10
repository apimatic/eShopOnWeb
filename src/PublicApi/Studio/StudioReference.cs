using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MindbodyPublicApi.Core;
using MindbodyPublicApi.Core.Models;
using MindbodyPublicApi.Models;
using MindbodyPublicApi.Requests.ClientApi;
using MindbodyPublicApi.Requests.SiteApi;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Process-wide state shared by all requests: slow-changing studio reference data (kept fresh,
/// because the app runs for weeks) and the per-client locks that make retried requests safe.
/// </summary>
public sealed class StudioReference
{
    private readonly MindbodyGateway _gateway;

    private readonly RefreshingValue<TimeZoneInfo> _timeZone = new(TimeSpan.FromHours(1));
    private readonly RefreshingValue<IReadOnlyList<SessionType>> _sessionTypes = new(TimeSpan.FromMinutes(10));
    private readonly RefreshingValue<IReadOnlyList<Location>> _locations = new(TimeSpan.FromMinutes(10));
    private readonly RefreshingValue<IReadOnlyList<string>> _referralTypes = new(TimeSpan.FromMinutes(10));

    internal KeyedLock Locks { get; } = new();

    public StudioReference(MindbodyGateway gateway) => _gateway = gateway;

    /// <summary>The studio's own time zone: Mindbody reports every schedule time as a local wall-clock time of the site.</summary>
    public Task<TimeZoneInfo> GetTimeZoneAsync() => _timeZone.GetAsync(async () =>
    {
        var response = await _gateway.CallAsync((client, token) => client.SiteApi.GetSites(new GetSitesOperationRequest
        {
            Authorization = token,
            RequestSiteIds = new[] { int.Parse(_gateway.SiteId, System.Globalization.CultureInfo.InvariantCulture) },
        }, new RequestOptions { Headers = new[] { new HeaderParam("siteId", _gateway.SiteId) } })).ConfigureAwait(false);

        var id = response.Sites?.FirstOrDefault()?.TimeZone;
        if (string.IsNullOrWhiteSpace(id))
            throw new StudioException(502, "The studio service did not report its time zone.");
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new StudioException(502, $"The studio's time zone '{id}' is not known to this server.");
        }
    });

    public Task<IReadOnlyList<SessionType>> GetSessionTypesAsync() => _sessionTypes.GetAsync(async () =>
    {
        var all = await _gateway.CallAsync((client, token) => MindbodyGateway.ReadAllAsync(
            (offset, limit) => client.SiteApi.GetSessionTypes(new GetSessionTypesOperationRequest
            {
                SiteId = _gateway.SiteId,
                Authorization = token,
                RequestLimit = limit,
                RequestOffset = offset,
            }),
            page => page.SessionTypes,
            page => page.PaginationResponse?.TotalResults)).ConfigureAwait(false);
        return (IReadOnlyList<SessionType>)all;
    });

    public Task<IReadOnlyList<Location>> GetLocationsAsync() => _locations.GetAsync(async () =>
    {
        var all = await _gateway.CallAsync((client, token) => MindbodyGateway.ReadAllAsync(
            (offset, limit) => client.SiteApi.GetLocations(new GetLocationsOperationRequest
            {
                SiteId = _gateway.SiteId,
                Authorization = token,
                RequestLimit = limit,
                RequestOffset = offset,
            }),
            page => page.Locations,
            page => page.PaginationResponse?.TotalResults)).ConfigureAwait(false);
        return (IReadOnlyList<Location>)all;
    });

    public Task<IReadOnlyList<string>> GetReferralTypesAsync() => _referralTypes.GetAsync(async () =>
    {
        var response = await _gateway.CallAsync((client, token) => client.ClientApi.GetClientReferralTypes(
            new GetClientReferralTypesOperationRequest { SiteId = _gateway.SiteId, Authorization = token })).ConfigureAwait(false);
        return (IReadOnlyList<string>)(response.ReferralTypes ?? new List<string>());
    });
}
