using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

public sealed class Api20100401Stream
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Stream(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a Stream
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created this Stream resource.</param>
    /// <param name="callSid">The SID of the <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> the Stream resource is associated with.</param>
    /// <param name="url"></param>
    /// <param name="name"></param>
    /// <param name="track"></param>
    /// <param name="statusCallback"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="parameter1Name"></param>
    /// <param name="parameter1Value"></param>
    /// <param name="parameter2Name"></param>
    /// <param name="parameter2Value"></param>
    /// <param name="parameter3Name"></param>
    /// <param name="parameter3Value"></param>
    /// <param name="parameter4Name"></param>
    /// <param name="parameter4Value"></param>
    /// <param name="parameter5Name"></param>
    /// <param name="parameter5Value"></param>
    /// <param name="parameter6Name"></param>
    /// <param name="parameter6Value"></param>
    /// <param name="parameter7Name"></param>
    /// <param name="parameter7Value"></param>
    /// <param name="parameter8Name"></param>
    /// <param name="parameter8Value"></param>
    /// <param name="parameter9Name"></param>
    /// <param name="parameter9Value"></param>
    /// <param name="parameter10Name"></param>
    /// <param name="parameter10Value"></param>
    /// <param name="parameter11Name"></param>
    /// <param name="parameter11Value"></param>
    /// <param name="parameter12Name"></param>
    /// <param name="parameter12Value"></param>
    /// <param name="parameter13Name"></param>
    /// <param name="parameter13Value"></param>
    /// <param name="parameter14Name"></param>
    /// <param name="parameter14Value"></param>
    /// <param name="parameter15Name"></param>
    /// <param name="parameter15Value"></param>
    /// <param name="parameter16Name"></param>
    /// <param name="parameter16Value"></param>
    /// <param name="parameter17Name"></param>
    /// <param name="parameter17Value"></param>
    /// <param name="parameter18Name"></param>
    /// <param name="parameter18Value"></param>
    /// <param name="parameter19Name"></param>
    /// <param name="parameter19Value"></param>
    /// <param name="parameter20Name"></param>
    /// <param name="parameter20Value"></param>
    /// <param name="parameter21Name"></param>
    /// <param name="parameter21Value"></param>
    /// <param name="parameter22Name"></param>
    /// <param name="parameter22Value"></param>
    /// <param name="parameter23Name"></param>
    /// <param name="parameter23Value"></param>
    /// <param name="parameter24Name"></param>
    /// <param name="parameter24Value"></param>
    /// <param name="parameter25Name"></param>
    /// <param name="parameter25Value"></param>
    /// <param name="parameter26Name"></param>
    /// <param name="parameter26Value"></param>
    /// <param name="parameter27Name"></param>
    /// <param name="parameter27Value"></param>
    /// <param name="parameter28Name"></param>
    /// <param name="parameter28Value"></param>
    /// <param name="parameter29Name"></param>
    /// <param name="parameter29Value"></param>
    /// <param name="parameter30Name"></param>
    /// <param name="parameter30Value"></param>
    /// <param name="parameter31Name"></param>
    /// <param name="parameter31Value"></param>
    /// <param name="parameter32Name"></param>
    /// <param name="parameter32Value"></param>
    /// <param name="parameter33Name"></param>
    /// <param name="parameter33Value"></param>
    /// <param name="parameter34Name"></param>
    /// <param name="parameter34Value"></param>
    /// <param name="parameter35Name"></param>
    /// <param name="parameter35Value"></param>
    /// <param name="parameter36Name"></param>
    /// <param name="parameter36Value"></param>
    /// <param name="parameter37Name"></param>
    /// <param name="parameter37Value"></param>
    /// <param name="parameter38Name"></param>
    /// <param name="parameter38Value"></param>
    /// <param name="parameter39Name"></param>
    /// <param name="parameter39Value"></param>
    /// <param name="parameter40Name"></param>
    /// <param name="parameter40Value"></param>
    /// <param name="parameter41Name"></param>
    /// <param name="parameter41Value"></param>
    /// <param name="parameter42Name"></param>
    /// <param name="parameter42Value"></param>
    /// <param name="parameter43Name"></param>
    /// <param name="parameter43Value"></param>
    /// <param name="parameter44Name"></param>
    /// <param name="parameter44Value"></param>
    /// <param name="parameter45Name"></param>
    /// <param name="parameter45Value"></param>
    /// <param name="parameter46Name"></param>
    /// <param name="parameter46Value"></param>
    /// <param name="parameter47Name"></param>
    /// <param name="parameter47Value"></param>
    /// <param name="parameter48Name"></param>
    /// <param name="parameter48Value"></param>
    /// <param name="parameter49Name"></param>
    /// <param name="parameter49Value"></param>
    /// <param name="parameter50Name"></param>
    /// <param name="parameter50Value"></param>
    /// <param name="parameter51Name"></param>
    /// <param name="parameter51Value"></param>
    /// <param name="parameter52Name"></param>
    /// <param name="parameter52Value"></param>
    /// <param name="parameter53Name"></param>
    /// <param name="parameter53Value"></param>
    /// <param name="parameter54Name"></param>
    /// <param name="parameter54Value"></param>
    /// <param name="parameter55Name"></param>
    /// <param name="parameter55Value"></param>
    /// <param name="parameter56Name"></param>
    /// <param name="parameter56Value"></param>
    /// <param name="parameter57Name"></param>
    /// <param name="parameter57Value"></param>
    /// <param name="parameter58Name"></param>
    /// <param name="parameter58Value"></param>
    /// <param name="parameter59Name"></param>
    /// <param name="parameter59Value"></param>
    /// <param name="parameter60Name"></param>
    /// <param name="parameter60Value"></param>
    /// <param name="parameter61Name"></param>
    /// <param name="parameter61Value"></param>
    /// <param name="parameter62Name"></param>
    /// <param name="parameter62Value"></param>
    /// <param name="parameter63Name"></param>
    /// <param name="parameter63Value"></param>
    /// <param name="parameter64Name"></param>
    /// <param name="parameter64Value"></param>
    /// <param name="parameter65Name"></param>
    /// <param name="parameter65Value"></param>
    /// <param name="parameter66Name"></param>
    /// <param name="parameter66Value"></param>
    /// <param name="parameter67Name"></param>
    /// <param name="parameter67Value"></param>
    /// <param name="parameter68Name"></param>
    /// <param name="parameter68Value"></param>
    /// <param name="parameter69Name"></param>
    /// <param name="parameter69Value"></param>
    /// <param name="parameter70Name"></param>
    /// <param name="parameter70Value"></param>
    /// <param name="parameter71Name"></param>
    /// <param name="parameter71Value"></param>
    /// <param name="parameter72Name"></param>
    /// <param name="parameter72Value"></param>
    /// <param name="parameter73Name"></param>
    /// <param name="parameter73Value"></param>
    /// <param name="parameter74Name"></param>
    /// <param name="parameter74Value"></param>
    /// <param name="parameter75Name"></param>
    /// <param name="parameter75Value"></param>
    /// <param name="parameter76Name"></param>
    /// <param name="parameter76Value"></param>
    /// <param name="parameter77Name"></param>
    /// <param name="parameter77Value"></param>
    /// <param name="parameter78Name"></param>
    /// <param name="parameter78Value"></param>
    /// <param name="parameter79Name"></param>
    /// <param name="parameter79Value"></param>
    /// <param name="parameter80Name"></param>
    /// <param name="parameter80Value"></param>
    /// <param name="parameter81Name"></param>
    /// <param name="parameter81Value"></param>
    /// <param name="parameter82Name"></param>
    /// <param name="parameter82Value"></param>
    /// <param name="parameter83Name"></param>
    /// <param name="parameter83Value"></param>
    /// <param name="parameter84Name"></param>
    /// <param name="parameter84Value"></param>
    /// <param name="parameter85Name"></param>
    /// <param name="parameter85Value"></param>
    /// <param name="parameter86Name"></param>
    /// <param name="parameter86Value"></param>
    /// <param name="parameter87Name"></param>
    /// <param name="parameter87Value"></param>
    /// <param name="parameter88Name"></param>
    /// <param name="parameter88Value"></param>
    /// <param name="parameter89Name"></param>
    /// <param name="parameter89Value"></param>
    /// <param name="parameter90Name"></param>
    /// <param name="parameter90Value"></param>
    /// <param name="parameter91Name"></param>
    /// <param name="parameter91Value"></param>
    /// <param name="parameter92Name"></param>
    /// <param name="parameter92Value"></param>
    /// <param name="parameter93Name"></param>
    /// <param name="parameter93Value"></param>
    /// <param name="parameter94Name"></param>
    /// <param name="parameter94Value"></param>
    /// <param name="parameter95Name"></param>
    /// <param name="parameter95Value"></param>
    /// <param name="parameter96Name"></param>
    /// <param name="parameter96Value"></param>
    /// <param name="parameter97Name"></param>
    /// <param name="parameter97Value"></param>
    /// <param name="parameter98Name"></param>
    /// <param name="parameter98Value"></param>
    /// <param name="parameter99Name"></param>
    /// <param name="parameter99Value"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountCallStream"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a Stream
    /// </remarks>
    public Task<ApiV2010AccountCallStream> CreateStream(string accountSid,
        string callSid,
        string url,
        string? name,
        StreamEnumTrack? track,
        string? statusCallback,
        StatusCallbackMethod19? statusCallbackMethod,
        string? parameter1Name,
        string? parameter1Value,
        string? parameter2Name,
        string? parameter2Value,
        string? parameter3Name,
        string? parameter3Value,
        string? parameter4Name,
        string? parameter4Value,
        string? parameter5Name,
        string? parameter5Value,
        string? parameter6Name,
        string? parameter6Value,
        string? parameter7Name,
        string? parameter7Value,
        string? parameter8Name,
        string? parameter8Value,
        string? parameter9Name,
        string? parameter9Value,
        string? parameter10Name,
        string? parameter10Value,
        string? parameter11Name,
        string? parameter11Value,
        string? parameter12Name,
        string? parameter12Value,
        string? parameter13Name,
        string? parameter13Value,
        string? parameter14Name,
        string? parameter14Value,
        string? parameter15Name,
        string? parameter15Value,
        string? parameter16Name,
        string? parameter16Value,
        string? parameter17Name,
        string? parameter17Value,
        string? parameter18Name,
        string? parameter18Value,
        string? parameter19Name,
        string? parameter19Value,
        string? parameter20Name,
        string? parameter20Value,
        string? parameter21Name,
        string? parameter21Value,
        string? parameter22Name,
        string? parameter22Value,
        string? parameter23Name,
        string? parameter23Value,
        string? parameter24Name,
        string? parameter24Value,
        string? parameter25Name,
        string? parameter25Value,
        string? parameter26Name,
        string? parameter26Value,
        string? parameter27Name,
        string? parameter27Value,
        string? parameter28Name,
        string? parameter28Value,
        string? parameter29Name,
        string? parameter29Value,
        string? parameter30Name,
        string? parameter30Value,
        string? parameter31Name,
        string? parameter31Value,
        string? parameter32Name,
        string? parameter32Value,
        string? parameter33Name,
        string? parameter33Value,
        string? parameter34Name,
        string? parameter34Value,
        string? parameter35Name,
        string? parameter35Value,
        string? parameter36Name,
        string? parameter36Value,
        string? parameter37Name,
        string? parameter37Value,
        string? parameter38Name,
        string? parameter38Value,
        string? parameter39Name,
        string? parameter39Value,
        string? parameter40Name,
        string? parameter40Value,
        string? parameter41Name,
        string? parameter41Value,
        string? parameter42Name,
        string? parameter42Value,
        string? parameter43Name,
        string? parameter43Value,
        string? parameter44Name,
        string? parameter44Value,
        string? parameter45Name,
        string? parameter45Value,
        string? parameter46Name,
        string? parameter46Value,
        string? parameter47Name,
        string? parameter47Value,
        string? parameter48Name,
        string? parameter48Value,
        string? parameter49Name,
        string? parameter49Value,
        string? parameter50Name,
        string? parameter50Value,
        string? parameter51Name,
        string? parameter51Value,
        string? parameter52Name,
        string? parameter52Value,
        string? parameter53Name,
        string? parameter53Value,
        string? parameter54Name,
        string? parameter54Value,
        string? parameter55Name,
        string? parameter55Value,
        string? parameter56Name,
        string? parameter56Value,
        string? parameter57Name,
        string? parameter57Value,
        string? parameter58Name,
        string? parameter58Value,
        string? parameter59Name,
        string? parameter59Value,
        string? parameter60Name,
        string? parameter60Value,
        string? parameter61Name,
        string? parameter61Value,
        string? parameter62Name,
        string? parameter62Value,
        string? parameter63Name,
        string? parameter63Value,
        string? parameter64Name,
        string? parameter64Value,
        string? parameter65Name,
        string? parameter65Value,
        string? parameter66Name,
        string? parameter66Value,
        string? parameter67Name,
        string? parameter67Value,
        string? parameter68Name,
        string? parameter68Value,
        string? parameter69Name,
        string? parameter69Value,
        string? parameter70Name,
        string? parameter70Value,
        string? parameter71Name,
        string? parameter71Value,
        string? parameter72Name,
        string? parameter72Value,
        string? parameter73Name,
        string? parameter73Value,
        string? parameter74Name,
        string? parameter74Value,
        string? parameter75Name,
        string? parameter75Value,
        string? parameter76Name,
        string? parameter76Value,
        string? parameter77Name,
        string? parameter77Value,
        string? parameter78Name,
        string? parameter78Value,
        string? parameter79Name,
        string? parameter79Value,
        string? parameter80Name,
        string? parameter80Value,
        string? parameter81Name,
        string? parameter81Value,
        string? parameter82Name,
        string? parameter82Value,
        string? parameter83Name,
        string? parameter83Value,
        string? parameter84Name,
        string? parameter84Value,
        string? parameter85Name,
        string? parameter85Value,
        string? parameter86Name,
        string? parameter86Value,
        string? parameter87Name,
        string? parameter87Value,
        string? parameter88Name,
        string? parameter88Value,
        string? parameter89Name,
        string? parameter89Value,
        string? parameter90Name,
        string? parameter90Value,
        string? parameter91Name,
        string? parameter91Value,
        string? parameter92Name,
        string? parameter92Value,
        string? parameter93Name,
        string? parameter93Value,
        string? parameter94Name,
        string? parameter94Value,
        string? parameter95Name,
        string? parameter95Value,
        string? parameter96Name,
        string? parameter96Value,
        string? parameter97Name,
        string? parameter97Value,
        string? parameter98Name,
        string? parameter98Value,
        string? parameter99Name,
        string? parameter99Value,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/Streams.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("CallSid", callSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Url", url),
                    new Param("Name", name),
                    new Param("Track", track),
                    new Param("StatusCallback", statusCallback),
                    new Param("StatusCallbackMethod", statusCallbackMethod),
                    new Param("Parameter1.Name", parameter1Name),
                    new Param("Parameter1.Value", parameter1Value),
                    new Param("Parameter2.Name", parameter2Name),
                    new Param("Parameter2.Value", parameter2Value),
                    new Param("Parameter3.Name", parameter3Name),
                    new Param("Parameter3.Value", parameter3Value),
                    new Param("Parameter4.Name", parameter4Name),
                    new Param("Parameter4.Value", parameter4Value),
                    new Param("Parameter5.Name", parameter5Name),
                    new Param("Parameter5.Value", parameter5Value),
                    new Param("Parameter6.Name", parameter6Name),
                    new Param("Parameter6.Value", parameter6Value),
                    new Param("Parameter7.Name", parameter7Name),
                    new Param("Parameter7.Value", parameter7Value),
                    new Param("Parameter8.Name", parameter8Name),
                    new Param("Parameter8.Value", parameter8Value),
                    new Param("Parameter9.Name", parameter9Name),
                    new Param("Parameter9.Value", parameter9Value),
                    new Param("Parameter10.Name", parameter10Name),
                    new Param("Parameter10.Value", parameter10Value),
                    new Param("Parameter11.Name", parameter11Name),
                    new Param("Parameter11.Value", parameter11Value),
                    new Param("Parameter12.Name", parameter12Name),
                    new Param("Parameter12.Value", parameter12Value),
                    new Param("Parameter13.Name", parameter13Name),
                    new Param("Parameter13.Value", parameter13Value),
                    new Param("Parameter14.Name", parameter14Name),
                    new Param("Parameter14.Value", parameter14Value),
                    new Param("Parameter15.Name", parameter15Name),
                    new Param("Parameter15.Value", parameter15Value),
                    new Param("Parameter16.Name", parameter16Name),
                    new Param("Parameter16.Value", parameter16Value),
                    new Param("Parameter17.Name", parameter17Name),
                    new Param("Parameter17.Value", parameter17Value),
                    new Param("Parameter18.Name", parameter18Name),
                    new Param("Parameter18.Value", parameter18Value),
                    new Param("Parameter19.Name", parameter19Name),
                    new Param("Parameter19.Value", parameter19Value),
                    new Param("Parameter20.Name", parameter20Name),
                    new Param("Parameter20.Value", parameter20Value),
                    new Param("Parameter21.Name", parameter21Name),
                    new Param("Parameter21.Value", parameter21Value),
                    new Param("Parameter22.Name", parameter22Name),
                    new Param("Parameter22.Value", parameter22Value),
                    new Param("Parameter23.Name", parameter23Name),
                    new Param("Parameter23.Value", parameter23Value),
                    new Param("Parameter24.Name", parameter24Name),
                    new Param("Parameter24.Value", parameter24Value),
                    new Param("Parameter25.Name", parameter25Name),
                    new Param("Parameter25.Value", parameter25Value),
                    new Param("Parameter26.Name", parameter26Name),
                    new Param("Parameter26.Value", parameter26Value),
                    new Param("Parameter27.Name", parameter27Name),
                    new Param("Parameter27.Value", parameter27Value),
                    new Param("Parameter28.Name", parameter28Name),
                    new Param("Parameter28.Value", parameter28Value),
                    new Param("Parameter29.Name", parameter29Name),
                    new Param("Parameter29.Value", parameter29Value),
                    new Param("Parameter30.Name", parameter30Name),
                    new Param("Parameter30.Value", parameter30Value),
                    new Param("Parameter31.Name", parameter31Name),
                    new Param("Parameter31.Value", parameter31Value),
                    new Param("Parameter32.Name", parameter32Name),
                    new Param("Parameter32.Value", parameter32Value),
                    new Param("Parameter33.Name", parameter33Name),
                    new Param("Parameter33.Value", parameter33Value),
                    new Param("Parameter34.Name", parameter34Name),
                    new Param("Parameter34.Value", parameter34Value),
                    new Param("Parameter35.Name", parameter35Name),
                    new Param("Parameter35.Value", parameter35Value),
                    new Param("Parameter36.Name", parameter36Name),
                    new Param("Parameter36.Value", parameter36Value),
                    new Param("Parameter37.Name", parameter37Name),
                    new Param("Parameter37.Value", parameter37Value),
                    new Param("Parameter38.Name", parameter38Name),
                    new Param("Parameter38.Value", parameter38Value),
                    new Param("Parameter39.Name", parameter39Name),
                    new Param("Parameter39.Value", parameter39Value),
                    new Param("Parameter40.Name", parameter40Name),
                    new Param("Parameter40.Value", parameter40Value),
                    new Param("Parameter41.Name", parameter41Name),
                    new Param("Parameter41.Value", parameter41Value),
                    new Param("Parameter42.Name", parameter42Name),
                    new Param("Parameter42.Value", parameter42Value),
                    new Param("Parameter43.Name", parameter43Name),
                    new Param("Parameter43.Value", parameter43Value),
                    new Param("Parameter44.Name", parameter44Name),
                    new Param("Parameter44.Value", parameter44Value),
                    new Param("Parameter45.Name", parameter45Name),
                    new Param("Parameter45.Value", parameter45Value),
                    new Param("Parameter46.Name", parameter46Name),
                    new Param("Parameter46.Value", parameter46Value),
                    new Param("Parameter47.Name", parameter47Name),
                    new Param("Parameter47.Value", parameter47Value),
                    new Param("Parameter48.Name", parameter48Name),
                    new Param("Parameter48.Value", parameter48Value),
                    new Param("Parameter49.Name", parameter49Name),
                    new Param("Parameter49.Value", parameter49Value),
                    new Param("Parameter50.Name", parameter50Name),
                    new Param("Parameter50.Value", parameter50Value),
                    new Param("Parameter51.Name", parameter51Name),
                    new Param("Parameter51.Value", parameter51Value),
                    new Param("Parameter52.Name", parameter52Name),
                    new Param("Parameter52.Value", parameter52Value),
                    new Param("Parameter53.Name", parameter53Name),
                    new Param("Parameter53.Value", parameter53Value),
                    new Param("Parameter54.Name", parameter54Name),
                    new Param("Parameter54.Value", parameter54Value),
                    new Param("Parameter55.Name", parameter55Name),
                    new Param("Parameter55.Value", parameter55Value),
                    new Param("Parameter56.Name", parameter56Name),
                    new Param("Parameter56.Value", parameter56Value),
                    new Param("Parameter57.Name", parameter57Name),
                    new Param("Parameter57.Value", parameter57Value),
                    new Param("Parameter58.Name", parameter58Name),
                    new Param("Parameter58.Value", parameter58Value),
                    new Param("Parameter59.Name", parameter59Name),
                    new Param("Parameter59.Value", parameter59Value),
                    new Param("Parameter60.Name", parameter60Name),
                    new Param("Parameter60.Value", parameter60Value),
                    new Param("Parameter61.Name", parameter61Name),
                    new Param("Parameter61.Value", parameter61Value),
                    new Param("Parameter62.Name", parameter62Name),
                    new Param("Parameter62.Value", parameter62Value),
                    new Param("Parameter63.Name", parameter63Name),
                    new Param("Parameter63.Value", parameter63Value),
                    new Param("Parameter64.Name", parameter64Name),
                    new Param("Parameter64.Value", parameter64Value),
                    new Param("Parameter65.Name", parameter65Name),
                    new Param("Parameter65.Value", parameter65Value),
                    new Param("Parameter66.Name", parameter66Name),
                    new Param("Parameter66.Value", parameter66Value),
                    new Param("Parameter67.Name", parameter67Name),
                    new Param("Parameter67.Value", parameter67Value),
                    new Param("Parameter68.Name", parameter68Name),
                    new Param("Parameter68.Value", parameter68Value),
                    new Param("Parameter69.Name", parameter69Name),
                    new Param("Parameter69.Value", parameter69Value),
                    new Param("Parameter70.Name", parameter70Name),
                    new Param("Parameter70.Value", parameter70Value),
                    new Param("Parameter71.Name", parameter71Name),
                    new Param("Parameter71.Value", parameter71Value),
                    new Param("Parameter72.Name", parameter72Name),
                    new Param("Parameter72.Value", parameter72Value),
                    new Param("Parameter73.Name", parameter73Name),
                    new Param("Parameter73.Value", parameter73Value),
                    new Param("Parameter74.Name", parameter74Name),
                    new Param("Parameter74.Value", parameter74Value),
                    new Param("Parameter75.Name", parameter75Name),
                    new Param("Parameter75.Value", parameter75Value),
                    new Param("Parameter76.Name", parameter76Name),
                    new Param("Parameter76.Value", parameter76Value),
                    new Param("Parameter77.Name", parameter77Name),
                    new Param("Parameter77.Value", parameter77Value),
                    new Param("Parameter78.Name", parameter78Name),
                    new Param("Parameter78.Value", parameter78Value),
                    new Param("Parameter79.Name", parameter79Name),
                    new Param("Parameter79.Value", parameter79Value),
                    new Param("Parameter80.Name", parameter80Name),
                    new Param("Parameter80.Value", parameter80Value),
                    new Param("Parameter81.Name", parameter81Name),
                    new Param("Parameter81.Value", parameter81Value),
                    new Param("Parameter82.Name", parameter82Name),
                    new Param("Parameter82.Value", parameter82Value),
                    new Param("Parameter83.Name", parameter83Name),
                    new Param("Parameter83.Value", parameter83Value),
                    new Param("Parameter84.Name", parameter84Name),
                    new Param("Parameter84.Value", parameter84Value),
                    new Param("Parameter85.Name", parameter85Name),
                    new Param("Parameter85.Value", parameter85Value),
                    new Param("Parameter86.Name", parameter86Name),
                    new Param("Parameter86.Value", parameter86Value),
                    new Param("Parameter87.Name", parameter87Name),
                    new Param("Parameter87.Value", parameter87Value),
                    new Param("Parameter88.Name", parameter88Name),
                    new Param("Parameter88.Value", parameter88Value),
                    new Param("Parameter89.Name", parameter89Name),
                    new Param("Parameter89.Value", parameter89Value),
                    new Param("Parameter90.Name", parameter90Name),
                    new Param("Parameter90.Value", parameter90Value),
                    new Param("Parameter91.Name", parameter91Name),
                    new Param("Parameter91.Value", parameter91Value),
                    new Param("Parameter92.Name", parameter92Name),
                    new Param("Parameter92.Value", parameter92Value),
                    new Param("Parameter93.Name", parameter93Name),
                    new Param("Parameter93.Value", parameter93Value),
                    new Param("Parameter94.Name", parameter94Name),
                    new Param("Parameter94.Value", parameter94Value),
                    new Param("Parameter95.Name", parameter95Name),
                    new Param("Parameter95.Value", parameter95Value),
                    new Param("Parameter96.Name", parameter96Name),
                    new Param("Parameter96.Value", parameter96Value),
                    new Param("Parameter97.Name", parameter97Name),
                    new Param("Parameter97.Value", parameter97Value),
                    new Param("Parameter98.Name", parameter98Name),
                    new Param("Parameter98.Value", parameter98Value),
                    new Param("Parameter99.Name", parameter99Name),
                    new Param("Parameter99.Value", parameter99Value)]),
            JsonResponse.Create<ApiV2010AccountCallStream>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Stop a Stream using either the SID of the Stream resource or the <c>name</c> used when creating the resource
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created this Stream resource.</param>
    /// <param name="callSid">The SID of the <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> the Stream resource is associated with.</param>
    /// <param name="sid">The SID or the <c>name</c> of the Stream resource to be stopped</param>
    /// <param name="status"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountCallStream"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Stop a Stream using either the SID of the Stream resource or the <c>name</c> used when creating the resource
    /// </remarks>
    public Task<ApiV2010AccountCallStream> UpdateStream(string accountSid,
        string callSid,
        string sid,
        StreamEnumUpdateStatus status,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/Streams/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("CallSid", callSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Status", status)]),
            JsonResponse.Create<ApiV2010AccountCallStream>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
