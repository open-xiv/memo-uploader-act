using System;
using System.Collections.Generic;
using System.Linq;
using Advanced_Combat_Tracker;
using MemoUploader.Models;


namespace MemoUploader.Events;

public class PlayerSnapshot
{
    public uint   EntityId { get; set; }
    public string Name     { get; set; } = string.Empty;
    public string Server   { get; set; } = string.Empty;
    public uint   JobId    { get; set; }
    public uint   Level    { get; set; }
}

public interface IPartyProvider
{
    IReadOnlyCollection<PlayerSnapshot> GetPartySnapshots();
}

public class ActPartyProvider : IPartyProvider
{
    public Dictionary<uint, PlayerSnapshot> PlayerCache    = [];
    public HashSet<uint>                    PartyPlayerIds = [];

    public IReadOnlyCollection<PlayerSnapshot> GetPartySnapshots()
        => PlayerCache.Values.Where(p => PartyPlayerIds.Contains(p.EntityId)).ToArray();

    public List<PlayerPayload> GetPartyPayload(EncounterData encounter)
    {
        var players = new List<PlayerPayload>();
        foreach (var player in GetPartySnapshots())
        {
            var combatant = encounter.Items.Values.FirstOrDefault(x => x.Name == player.Name);
            if (combatant is null)
                continue;

            var deathCount = (uint)Math.Max(0, combatant.Deaths);
            players.Add(new PlayerPayload
            {
                Name       = player.Name,
                Server     = player.Server,
                JobId      = player.JobId,
                Level      = player.Level,
                DeathCount = deathCount
            });
        }

        return players;
    }
}
