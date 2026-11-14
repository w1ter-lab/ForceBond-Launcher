using System.Security.Cryptography;
using System.Text;
namespace ForceBond.Launcher;
internal static class AccessTransport
{
    internal static bool ValidOrigin(string value) => Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme=="https" && uri.UserInfo.Length==0 && uri.AbsolutePath=="/" && uri.Query.Length==0 && uri.Fragment.Length==0 && value==uri.GetLeftPart(UriPartial.Authority);
    internal static bool SameOrigin(string origin,Uri address) => ValidOrigin(origin) && address.Scheme=="https" && address.UserInfo.Length==0 && address.Fragment.Length==0 && string.Equals(address.GetLeftPart(UriPartial.Authority),origin,StringComparison.OrdinalIgnoreCase);
    internal static string RandomVerifier() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
    internal static string Challenge(string verifier) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(verifier))).TrimEnd('=').Replace('+','-').Replace('/','_');
    internal static bool ValidAuthorization(string value,string origin,string clientId)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host!="discord.com" || uri.Port!=443 || uri.UserInfo.Length!=0 || uri.AbsolutePath!="/oauth2/authorize" || uri.Fragment.Length!=0)return false;
        try {
            var query=uri.Query.TrimStart('?').Split('&').Select(part=>part.Split('=',2)).ToDictionary(parts=>Uri.UnescapeDataString(parts[0]),parts=>parts.Length==2?Uri.UnescapeDataString(parts[1].Replace('+',' ')):"",StringComparer.Ordinal);
            return query.GetValueOrDefault("client_id")==clientId && query.GetValueOrDefault("redirect_uri")==origin+"/oauth/callback" && query.GetValueOrDefault("response_type")=="code" && query.GetValueOrDefault("scope")=="identify guilds.members.read";
        } catch {return false;}
    }
}
