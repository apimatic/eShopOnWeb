using System.Net.Http;
using System.Net.Http.Json;

namespace PayPalServerSdk.Core.Request;

internal sealed class JsonRequest<TData> : IRequest
{
    private readonly TData _data;

    public JsonRequest(TData data) => _data = data;

    public HttpContent Get() => JsonContent.Create(_data);
    public bool CanRetry => true;
}

internal static class JsonRequest
{
    public static JsonRequest<TData> Create<TData>(TData data) => new(data);
}