using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;
public sealed class PayPalClient
{
 private readonly IHttpClientFactory _factory; private readonly PayPalOptions _options; private readonly SemaphoreSlim _tokenLock = new(1,1); private string? _token; private DateTimeOffset _tokenExpires;
 public PayPalClient(IHttpClientFactory factory,IOptions<PayPalOptions> options){_factory=factory;_options=options.Value;}
 private async Task<string> AccessToken(CancellationToken ct){ if (_token is not null && _tokenExpires>DateTimeOffset.UtcNow.AddMinutes(1)) return _token; await _tokenLock.WaitAsync(ct); try { if (_token is not null && _tokenExpires>DateTimeOffset.UtcNow.AddMinutes(1)) return _token; using var c=_factory.CreateClient(); using var req=new HttpRequestMessage(HttpMethod.Post,_options.ApiBase+"/v1/oauth2/token"); req.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(_options.ClientId+":"+_options.ClientSecret))); req.Content=new FormUrlEncodedContent(new[]{new KeyValuePair<string,string>("grant_type","client_credentials")}); using var res=await c.SendAsync(req,ct); var body=await res.Content.ReadAsStringAsync(ct); if(!res.IsSuccessStatusCode) throw new InvalidOperationException("PayPal authentication failed."); using var doc=JsonDocument.Parse(body); _token=doc.RootElement.GetProperty("access_token").GetString(); _tokenExpires=DateTimeOffset.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32()); return _token!; } finally { _tokenLock.Release(); } }
 public async Task<JsonDocument> Send(HttpMethod method,string path,object? payload,string requestId,CancellationToken ct){ using var c=_factory.CreateClient(); using var req=new HttpRequestMessage(method,_options.ApiBase+path); req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",await AccessToken(ct)); req.Headers.Add("PayPal-Request-Id",requestId); req.Headers.Add("Prefer","return=representation"); req.Headers.Add("PayPal-Enforce-ISO8601-Format","true"); if(payload is not null) req.Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json"); using var res=await c.SendAsync(req,ct); var body=await res.Content.ReadAsStringAsync(ct); if(!res.IsSuccessStatusCode) { string detail="PayPal rejected the request"; try { using var d=JsonDocument.Parse(body); if(d.RootElement.TryGetProperty("message",out var m)) detail=m.GetString()??detail; if(d.RootElement.TryGetProperty("details",out var ds)) detail += ": " + string.Join(", ",ds.EnumerateArray().Select(x=>x.TryGetProperty("issue",out var i)?i.GetString():null).Where(x=>x is not null)); } catch {} throw new PayPalException((int)res.StatusCode,detail); } return JsonDocument.Parse(body.Length==0?"{}":body); }
 public PayPalOptions Options => _options;
}
public sealed class PayPalException : Exception { public int StatusCode {get;} public PayPalException(int status,string message):base(message){StatusCode=status;} }
