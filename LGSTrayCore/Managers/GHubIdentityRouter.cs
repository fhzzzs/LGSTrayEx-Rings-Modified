namespace LGSTrayCore.Managers;

// The protocol can report the same model for multiple real units. A model
// name alone is not a device identifier. Keep raw IDs separate whenever a
// list or concurrent registration proves ambiguity; serial-confirmed modes
// may still share their stable identity.
internal sealed class GHubIdentityRouter
{
    private sealed record Route(string Base,string Id,bool Confirmed);
    private readonly object gate=new();
    private readonly Dictionary<string,Route> known=new(StringComparer.Ordinal);
    private readonly HashSet<string> active=new(StringComparer.Ordinal);
    private readonly HashSet<string> ambiguous=new(StringComparer.Ordinal);
    internal void BeginList(IEnumerable<(string Raw,string Base,bool Confirmed)> records){
        lock(gate){
            foreach(var group in records.Where(x=>!x.Confirmed).GroupBy(x=>x.Base,StringComparer.Ordinal))
                if(group.Select(x=>x.Raw).Distinct(StringComparer.Ordinal).Skip(1).Any())ambiguous.Add(group.Key);
            active.Clear();
        }
    }
    internal string Register(string raw,string identity,bool confirmed){
        lock(gate){
            if(!confirmed&&active.Any(k=>k!=raw&&known.TryGetValue(k,out var other)&&other.Base==identity))ambiguous.Add(identity);
            string id=identity;
            if(!confirmed&&ambiguous.Contains(identity)){
                // Keep an existing route stable for the current session, even
                // if a second physical unit is discovered later.
                id=known.TryGetValue(raw,out var old)&&old.Base==identity?old.Id:identity+"#raw:"+raw;
            }
            known[raw]=new(identity,id,confirmed);active.Add(raw);return id;
        }
    }
    internal string Resolve(string raw){lock(gate)return known.TryGetValue(raw,out var route)?route.Id:raw;}
    internal bool Disconnect(string raw,out string identity){
        lock(gate){
            identity=known.TryGetValue(raw,out var route)?route.Id:raw;
            active.Remove(raw);
            string id=identity;
            return !active.Any(k=>known.TryGetValue(k,out var other)&&other.Id==id);
        }
    }
    internal void DisconnectedAll(){lock(gate)active.Clear();}
}
