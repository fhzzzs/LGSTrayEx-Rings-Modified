using System;
using System.Linq;
using System.Text;
namespace LGSTrayPrimitives;
public static class HardwareIdentity
{
    // Exact, vendor-scoped serials only. Never derive physical identity from a model name.
    public static string? LogitechSerial(string? value,bool fromHid=false){
        if(string.IsNullOrWhiteSpace(value))return null;
        value=value.Trim();
        if(fromHid){try{var bytes=Convert.FromHexString(value);var printable=bytes.TakeWhile(b=>b!=0).ToArray();if(printable.Length>=6&&printable.All(b=>b>=32&&b<=126)&&bytes.Skip(printable.Length).All(b=>b==0))value=Encoding.ASCII.GetString(printable).Trim();else return null;}catch{return null;}}
        value=value.ToUpperInvariant();
        if(value.Length<6||value.Length>64||value.Distinct().Count()<3||value.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-')||value is "UNKNOWN" or "DEFAULT" or "NOTFOUND")return null;
        return "logitech-serial:"+value;
    }
}
