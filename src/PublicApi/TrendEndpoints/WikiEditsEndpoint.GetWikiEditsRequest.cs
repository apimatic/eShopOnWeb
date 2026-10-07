namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

public class GetWikiEditsRequest : BaseRequest
{
    public const int MIN_SECONDS = 5;
    public const int MAX_SECONDS = 60;
    public const int DEFAULT_SECONDS = 20;

    public int Seconds { get; init; }

    public GetWikiEditsRequest(int seconds)
    {
        Seconds = seconds;
    }
}
