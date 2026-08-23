using System;
using System.Collections.Generic;

namespace LostScrollsII.Ranking
{
    // A posted, staked 1v1 challenge (docs/Wagers.md).
    //
    // The shape is deliberately FLAT — two named sides rather than a list of
    // participants — because a duel invite is always exactly two companions and
    // the flat form keeps the serializer, the RPCs and the panel simple. If team
    // invites are ever wanted they belong in the tournament model, which already
    // handles N sides.
    //
    // Both sides escrow their Communion Totem exactly like a tournament entrant:
    // the totem leaves the poster's inventory when they post, and the challenger's
    // when they accept. The server holds both payloads, summons the two companions
    // when both owners are ready, and hands the totems back at the end (the winner's
    // resealed with whatever XP it gained).
    [Serializable]
    public class DuelInvite
    {
        public string id = "";
        public string currency = "";   // "coins" | "valcoin"
        public int stake;

        // open      — posted, waiting for a challenger
        // matched   — accepted; both owners must mark themselves ready
        // running   — both companions summoned, the duel is under way
        public string phase = "open";

        public long hostId;
        public string hostName = "";
        public string hostEntrantId = "";
        public string hostLabel = "";
        public int hostLevel;
        public int hostCaste;
        public string hostPayload = "";
        public bool hostReady;

        public long oppId;
        public string oppName = "";
        public string oppEntrantId = "";
        public string oppLabel = "";
        public int oppLevel;
        public int oppCaste;
        public string oppPayload = "";
        public bool oppReady;

        public long createdTicks;

        // UTC ticks the duel was settled. Like a tournament, the totems are held for
        // a short grace afterwards so the two clients can reseal their live
        // companions first — otherwise the winner would get its pre-duel state back.
        public long completedTicks;

        public bool Involves(long ownerId) => ownerId != 0L && (hostId == ownerId || oppId == ownerId);

        public bool HasEntrant(string entrantId)
            => !string.IsNullOrEmpty(entrantId) && (hostEntrantId == entrantId || oppEntrantId == entrantId);

        public bool IsHostEntrant(string entrantId)
            => !string.IsNullOrEmpty(entrantId) && hostEntrantId == entrantId;
    }

    [Serializable]
    public class DuelInviteBoard
    {
        public List<DuelInvite> invites = new List<DuelInvite>();

        public DuelInvite Find(string id)
            => string.IsNullOrEmpty(id) ? null : invites.Find(i => i != null && i.id == id);

        // The one invite a player is currently tied up in, either as poster or
        // challenger. "A player can only post a duel invite once" is enforced by
        // refusing a second post while this returns non-null.
        public DuelInvite ForOwner(long ownerId)
            => ownerId == 0L ? null : invites.Find(i => i != null && i.Involves(ownerId));

        public DuelInvite ForEntrant(string entrantId)
            => string.IsNullOrEmpty(entrantId) ? null : invites.Find(i => i != null && i.HasEntrant(entrantId));
    }
}
