using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ForceBond.Updates;

namespace ForceBond.Launcher;
internal sealed record AccessService(string ServiceOrigin, string DiscordClientId);
internal sealed record AccessSession(string Origin, string Token, long ExpiresAt);
internal sealed record AccessConfiguration(int Schema, string ReleaseMode, bool LoginRequired, Dictionary<string,string> Notice);
internal sealed record AccessDecision(bool Allowed, string Reason, string? UserId, bool RoleplayAllowed = false);
internal static class BetaAccess
{
    internal static readonly AccessService Service = Load<AccessService>("ForceBond.AccessService");
    private static readonly JsonElement Policy = Load<JsonElement>("ForceBond.AccessPolicy");
    internal static bool Configured => AccessTransport.ValidOrigin(Service.ServiceOrigin);
    internal static bool PublicMode { get; private set; }
    private static string SessionPath => Path.Combine(LauncherSettings.DataDirectory, "discord-access.bin");
    internal static T Load<T>(string resource)
    {
        using var stream = typeof(BetaAccess).Assembly.GetManifestResourceStream(resource)!;
        return JsonSerializer.Deserialize<T>(stream, UpdateFiles.Json)!;
    }
    internal static string Notice => Policy.GetProperty("notices").GetProperty(L.Language is "ru" or "uk" ? L.Language : "en").GetString()!;
    internal static bool SameOrigin(Uri address) => Configured && AccessTransport.SameOrigin(Service.ServiceOrigin,address);
    private static readonly SocketsHttpHandler AccessHandler=new() {
        AllowAutoRedirect=false,
        PooledConnectionLifetime=TimeSpan.FromMinutes(5),
        ConnectTimeout=TimeSpan.FromSeconds(60)
    };
    internal static HttpClient Client() {
        var client=new HttpClient(AccessHandler,disposeHandler:false) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ForceBond-Launcher/1.0");
        return client;
    }
    private static string Url(string path) => Configured ? Service.ServiceOrigin+path : throw new IOException("The access service is not connected yet.");
    internal static AccessSession? Session()
    {
        try
        {
            UpdateFiles.NoLinks(SessionPath);
            var session = JsonSerializer.Deserialize<AccessSession>(UserProtectedData.Unprotect(File.ReadAllBytes(SessionPath)), UpdateFiles.Json);
            return session is not null && session.Origin == Service.ServiceOrigin && Regex.IsMatch(session.Token,"^[A-Za-z0-9_-]{43}$") && session.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() ? session : null;
        }
        catch { return null; }
    }
    private static void Store(AccessSession session)
    {
        UpdateFiles.NoLinks(SessionPath); Directory.CreateDirectory(LauncherSettings.DataDirectory);
        var bytes = UserProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(session,UpdateFiles.Json));
        string temporary = SessionPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllBytes(temporary,bytes);File.Move(temporary,SessionPath,true);
    }
    internal static async Task<HttpResponseMessage> SendAsync(HttpClient client,string url,CancellationToken token,bool authenticated=true)
    {
        Uri uri=UpdateFiles.Https(url);
        if(!SameOrigin(uri))throw new IOException("Update address does not match the access service.");
        var response = await AccessGetRetry.SendAsync(client, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if(authenticated && Session() is { } session)request.Headers.Authorization=new("Bearer",session.Token);
            return request;
        }, token, (attempt, status) => LauncherUpdates.Log($"Access GET {uri.AbsolutePath}: HTTP {(int)status}; retry {attempt}/2."));
        if (!response.IsSuccessStatusCode)
            LauncherUpdates.Log($"Access GET {uri.AbsolutePath}: HTTP {(int)response.StatusCode}.");
        return response;
    }
    private static async Task<T> Json<T>(HttpResponseMessage response,CancellationToken token)
    {
        using(response)
        {
            response.EnsureSuccessStatusCode();
            await using var input=await response.Content.ReadAsStreamAsync(token);
            using var output=new MemoryStream();byte[] buffer=new byte[4096];int count;
            while((count=await input.ReadAsync(buffer,token))!=0) { if(output.Length+count>131072)throw new IOException("Invalid access service response."); await output.WriteAsync(buffer.AsMemory(0,count),token); }
            return JsonSerializer.Deserialize<T>(output.ToArray(),UpdateFiles.Json) ?? throw new IOException("Invalid access service response.");
        }
    }
    internal static async Task<AccessConfiguration> Configuration(CancellationToken token)
    {
        using var client=Client();var configuration=await Json<AccessConfiguration>(await SendAsync(client,Url("/api/config"),token,false),token);
        if(configuration.Schema!=1 || configuration.ReleaseMode is not ("public" or "closed-beta") || configuration.LoginRequired!=(configuration.ReleaseMode=="closed-beta"))throw new IOException("Invalid access service response.");
        PublicMode=configuration.ReleaseMode=="public";return configuration;
    }
    internal static async Task<AccessDecision> Account(CancellationToken token)
    {
        var previous=Session();
        using var client=Client();
        var decision=await Json<AccessDecision>(await SendAsync(client,Url("/api/account"),token),token);
        // A token rejected by the server must not keep the sign-in button disabled.
        // A temporary verification failure keeps the saved session intact.
        if(decision.Reason=="sign-in-required" && previous is not null && Session()?.Token==previous.Token) {
            UpdateFiles.NoLinks(SessionPath);
            File.Delete(SessionPath);
            LauncherUpdates.Log("BETA_ACCESS_SESSION_EXPIRED");
        }
        return decision;
    }
    internal static async Task SignIn(CancellationToken token)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromMinutes(10));
        try {
        LauncherUpdates.Log("DISCORD_SIGNIN_STARTED");
        using var client=Client();
        string verifier=AccessTransport.RandomVerifier(),challenge=AccessTransport.Challenge(verifier);
        using var startRequest=new HttpRequestMessage(HttpMethod.Post,Url("/api/login/start")) { Content=JsonContent.Create(new { challenge }) };
        var start=await Json<LoginStart>(await client.SendAsync(startRequest,deadline.Token),deadline.Token);
        if(!Regex.IsMatch(start.Id,"^[A-Za-z0-9_-]{43}$") || !AccessTransport.ValidAuthorization(start.AuthorizationUrl,Service.ServiceOrigin,Service.DiscordClientId))throw new IOException("Invalid Discord sign-in address.");
        Process.Start(new ProcessStartInfo(start.AuthorizationUrl) { UseShellExecute=true })?.Dispose();
        LauncherUpdates.Log("DISCORD_BROWSER_OPENED");
        while(true)
        {
            await Task.Delay(2000,deadline.Token);
            using var poll=new HttpRequestMessage(HttpMethod.Post,Url("/api/login/poll")) { Content=JsonContent.Create(new { id=start.Id,verifier }) };
            var response=await client.SendAsync(poll,deadline.Token);
            if(response.StatusCode==System.Net.HttpStatusCode.Accepted) {response.Dispose();continue;}
            var session=await Json<LoginResult>(response,deadline.Token);
            if(!Regex.IsMatch(session.Token,"^[A-Za-z0-9_-]{43}$") || session.ExpiresAt<=DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new IOException("Invalid access service response.");
            deadline.Token.ThrowIfCancellationRequested();Store(new(Service.ServiceOrigin,session.Token,session.ExpiresAt));
            LauncherUpdates.Log("DISCORD_SESSION_SAVED");return;
        }
        } catch(OperationCanceledException) when(!token.IsCancellationRequested) {
            throw new TimeoutException("Discord sign-in timed out. Click Sign in with Discord to try again.");
        }
    }
    internal static async Task SignOut(CancellationToken token)
    {
        var session=Session();
        if(session is not null)
        {
            using var client=Client();using var request=new HttpRequestMessage(HttpMethod.Post,Url("/api/logout"));
            request.Headers.Authorization=new("Bearer",session.Token);
            using var response=await client.SendAsync(request,token);response.EnsureSuccessStatusCode();
        }
        if(File.Exists(SessionPath))File.Delete(SessionPath);
    }
    internal static string ManifestUrl => Url("/api/updates/latest");
    private sealed record LoginStart(string Id,string AuthorizationUrl,long ExpiresAt);
    private sealed record LoginResult(string Token,long ExpiresAt);
}
internal sealed class BetaAccessWindow : UtilityWindow
{
    private readonly CancellationTokenSource _stop=new();
    private readonly Label _status,_notice;
    private readonly StyledButton _signIn,_signOut,_check;
    private bool _updatesAllowed;
    internal bool CheckForUpdates { get; private set; }
    internal BetaAccessWindow():base("Closed beta access",new Size(620,460))
    {
        _notice=new Label { Text=BetaAccess.Notice,Location=new(24,78),Size=new(572,130),ForeColor=Ink,Font=new("Segoe UI",10) }; Controls.Add(_notice);
        _status=new Label { Text="Checking access...",Location=new(24,222),Size=new(572,64),ForeColor=Gold };Controls.Add(_status);
        _signIn=MakeButton("SIGN IN WITH DISCORD",new(24,302),new(572,40));
        _signOut=MakeButton("SIGN OUT",new(24,364),new(170,38));
        _check=MakeButton("CHECK FOR UPDATES",new(210,364),new(386,38));
        Controls.AddRange([_signIn,_signOut,_check]);
        _signIn.Enabled=_signOut.Enabled=_check.Enabled=false;
        _check.Click+=async(_,_)=>{
            if(!_updatesAllowed) {await Run(RefreshAccess);return;}
            CheckForUpdates=true;DialogResult=DialogResult.OK;Close();
        };
        _signIn.Click+=async(_,_)=>await Run(async()=>{_status.Text=L.T("Complete sign-in in your browser, then return here.");await BetaAccess.SignIn(_stop.Token);await RefreshAccess();});
        _signOut.Click+=async(_,_)=>await Run(async()=>{await BetaAccess.SignOut(_stop.Token);await RefreshAccess();});
        Shown+=async(_,_)=>await Run(RefreshAccess);
        FormClosing+=(_,_)=>_stop.Cancel();
        Disposed+=(_,_)=>_stop.Dispose();
        L.Apply(this);
    }
    private async Task RefreshAccess()
    {
        _updatesAllowed=false;
        if(!BetaAccess.Configured) {_status.Text=L.T("The access service is not connected yet.");return;}
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);deadline.CancelAfter(TimeSpan.FromSeconds(90));
        LauncherUpdates.Log("BETA_ACCESS_CONFIG_STARTED");
        var config=await BetaAccess.Configuration(deadline.Token);
        if(config.ReleaseMode=="public") {_updatesAllowed=true;_notice.Text=L.T("Force Bond is publicly available. No Discord sign-in is required to download or play.");_status.Text=L.T("Public access. Discord sign-in is not required.");return;}
        _notice.Text=BetaAccess.Notice;
        // Each stage gets its own budget: a slow configuration request must not
        // consume the time needed to validate an otherwise valid saved session.
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        LauncherUpdates.Log("BETA_ACCESS_ACCOUNT_STARTED");
        var decision=await BetaAccess.Account(deadline.Token);
        LauncherUpdates.Log("BETA_ACCESS_ACCOUNT_RESULT allowed="+decision.Allowed+" reason="+decision.Reason);
        if(decision.Reason=="verification-unavailable") {_status.Text=L.T("Could not verify access. Try again later. Installed builds still work.");return;}
        _updatesAllowed=decision.Allowed;
        _status.Text=L.T(decision.Allowed?"Beta access granted. Downloads and updates are available.":decision.Reason=="sign-in-required"?"Sign in with Discord to check beta access.":"Access requires the Playtester or Syndicate Corp. role on our Discord server.");
    }
    private async Task Run(Func<Task> operation)
    {
        _updatesAllowed=false;
        _signIn.Enabled=_signOut.Enabled=_check.Enabled=false;
        try {
            await operation();
            if(_updatesAllowed && !IsDisposed && !_stop.IsCancellationRequested) {
                CheckForUpdates=true;
                DialogResult=DialogResult.OK;
                Close();
            }
        }
        catch(TimeoutException) {
            LauncherUpdates.Log("DISCORD_SIGNIN_TIMEOUT");
            if(!IsDisposed && !_stop.IsCancellationRequested)_status.Text=L.T("Discord sign-in timed out. Click Sign in with Discord to try again.");
        }
        catch(OperationCanceledException) {
            if(!IsDisposed && !_stop.IsCancellationRequested) {
                LauncherUpdates.Log("BETA_ACCESS_CHECK_TIMEOUT sessionSaved="+(BetaAccess.Session() is not null));
                _status.Text=L.T(BetaAccess.Session() is not null
                    ? "Your sign-in is saved, but the access check timed out. Click Retry access check."
                    : "The access check timed out. Click Retry access check.");
            }
        }
        catch(Exception ex) {
            LauncherUpdates.Log("BETA_ACCESS_OPERATION_FAILED "+ex.GetType().Name+
                (ex is HttpRequestException http ? " HTTP="+http.StatusCode : "")+
                " sessionSaved="+(BetaAccess.Session() is not null));
            if(!IsDisposed)_status.Text=L.T("Could not verify access. Click Retry access check. Installed builds still work.");
        }
        finally
        {
            if(!IsDisposed) {
                _signIn.Visible=_signOut.Visible=!BetaAccess.PublicMode;
                bool signedIn=BetaAccess.Session() is not null;
                _signIn.Enabled=BetaAccess.Configured && !signedIn;
                _signOut.Enabled=signedIn;
                _check.Text=L.T(_updatesAllowed ? "CHECK FOR UPDATES" : "RETRY ACCESS CHECK");
                _check.Enabled=BetaAccess.Configured && !_stop.IsCancellationRequested;
            }
        }
    }
}
