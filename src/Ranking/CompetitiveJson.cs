using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LostScrollsII.Ranking
{
    // Hand-rolled JSON for the competitive data models (ladder, tournament,
    // champions). Replaces UnityEngine.JsonUtility, which on the Unity 6 runtime
    // Valheim now ships silently DROPS List<[Serializable] class> fields declared
    // in a plugin assembly — scalars survive, the lists vanish. That broke both
    // persistence (entrants/records missing from the per-world files) and the
    // server→client snapshot push (clients forever saw an empty board), verified
    // by the boot-time serializer self-test on the dedicated server.
    //
    // Explicit per-type mapping, no reflection, no engine dependence. The reader
    // is tolerant: missing keys keep defaults, so files written by the old
    // JsonUtility path (or older mod versions) still load.
    public static class CompetitiveJson
    {
        // ---- writers ----------------------------------------------------------

        public static string Write(LeaderboardData d, bool pretty = false)
        {
            d = d ?? new LeaderboardData();
            var w = new Writer(pretty);
            w.BeginObj();
            w.Field("seasonId", d.seasonId);
            w.Key("companions"); w.BeginArr();
            foreach (var c in d.companions ?? new List<CompanionRecord>()) WriteCompanion(w, c);
            w.EndArr();
            w.Key("parties"); w.BeginArr();
            foreach (var p in d.parties ?? new List<PartyRecord>()) WriteParty(w, p);
            w.EndArr();
            w.EndObj();
            return w.ToString();
        }

        // Several tournaments now run at once (one free slot + one per wager
        // currency), so the persisted/pushed document is a BOOK of them. The single
        // -tournament writer below is kept because it is still the unit of the
        // format, and because a file written by an older build is a bare tournament
        // object that the book reader has to be able to fall back to.
        public static string Write(TournamentBook b, bool pretty = false)
        {
            b = b ?? new TournamentBook();
            var w = new Writer(pretty);
            w.BeginObj();
            w.Key("tournaments"); w.BeginArr();
            foreach (var t in b.tournaments ?? new List<TournamentState>())
            {
                if (t == null) continue;
                WriteTournamentObj(w, t);
            }
            w.EndArr();
            w.EndObj();
            return w.ToString();
        }

        public static TournamentBook ReadTournamentBook(string json)
        {
            var book = new TournamentBook();
            var root = Parse(json) as Dictionary<string, object>;
            if (root == null) return book;

            if (root.ContainsKey("tournaments"))
            {
                foreach (var o in Arr(root, "tournaments"))
                    if (o is Dictionary<string, object> t) book.tournaments.Add(ReadTournamentObj(t));
                return book;
            }

            // Legacy shape: the whole document IS one tournament (pre-wager builds).
            // Load it into the free admin slot so an in-progress bracket survives
            // the upgrade rather than vanishing.
            var single = ReadTournamentObj(root);
            single.key = "";
            book.tournaments.Add(single);
            return book;
        }

        public static string Write(TournamentState s, bool pretty = false)
        {
            var w = new Writer(pretty);
            WriteTournamentObj(w, s ?? new TournamentState());
            return w.ToString();
        }

        private static void WriteTournamentObj(Writer w, TournamentState s)
        {
            w.BeginObj();
            w.Field("key", s.key);
            w.Field("currency", s.currency);
            w.Field("entryFee", s.entryFee);
            w.Field("prize", s.prize);
            w.Field("hostId", s.hostId);
            w.Field("hostName", s.hostName);
            w.Field("openedTicks", s.openedTicks);
            w.Field("hostCredit", s.hostCredit);
            w.Field("completedTicks", s.completedTicks);
            w.Field("active", s.active);
            w.Field("mode", s.mode);
            w.Field("phase", s.phase);
            w.Field("size", s.size);
            w.Field("bracketSize", s.bracketSize);
            w.Field("currentRound", s.currentRound);
            w.Field("championId", s.championId);
            w.Field("championLabel", s.championLabel);
            w.Field("seasonId", s.seasonId);
            w.Field("eliminationType", s.eliminationType);
            w.Field("totalRounds", s.totalRounds);
            w.Key("entrants"); w.BeginArr();
            foreach (var e in s.entrants ?? new List<TournamentEntrant>()) WriteEntrant(w, e);
            w.EndArr();
            w.Key("matches"); w.BeginArr();
            foreach (var m in s.matches ?? new List<TournamentMatch>()) WriteMatch(w, m);
            w.EndArr();
            w.EndObj();
        }

        // Bounty-hunter ladder (docs/Bounty-Hunting.md). Lives here rather than in a
        // second serializer under src/Bounty/ so there stays exactly ONE place that
        // knows how these files are written — the whole reason this class exists is
        // that the engine's serializer silently drops list fields, and duplicating a
        // parser would double the surface for that class of bug.
        public static string Write(Bounty.BountyLadderData d, bool pretty = false)
        {
            d = d ?? new Bounty.BountyLadderData();
            var w = new Writer(pretty);
            w.BeginObj();
            w.Field("seasonId", d.seasonId);
            w.Key("hunters"); w.BeginArr();
            foreach (var h in d.hunters ?? new List<Bounty.BountyHunterRecord>())
            {
                if (h == null) continue;
                w.BeginObj();
                w.Field("ownerId", h.ownerId);
                w.Field("ownerName", h.ownerName);
                w.Field("points", h.points);
                w.Field("kills", h.kills);
                w.Field("communes", h.communes);
                w.Field("bestTier", h.bestTier);
                w.Field("lastResolvedTicks", h.lastResolvedTicks);
                w.Field("seasonId", h.seasonId);
                w.EndObj();
            }
            w.EndArr();
            w.EndObj();
            return w.ToString();
        }

        public static Bounty.BountyLadderData ReadBountyLadder(string json)
        {
            var root = Parse(json) as Dictionary<string, object>;
            var d = new Bounty.BountyLadderData();
            if (root == null) return d;
            d.seasonId = I(root, "seasonId", d.seasonId);
            foreach (var o in Arr(root, "hunters"))
            {
                if (!(o is Dictionary<string, object> h)) continue;
                d.hunters.Add(new Bounty.BountyHunterRecord
                {
                    ownerId = L(h, "ownerId"),
                    ownerName = S(h, "ownerName"),
                    points = I(h, "points"),
                    kills = I(h, "kills"),
                    communes = I(h, "communes"),
                    bestTier = I(h, "bestTier"),
                    lastResolvedTicks = L(h, "lastResolvedTicks"),
                    seasonId = I(h, "seasonId", 1),
                });
            }
            return d;
        }

        // The Wanted Board's open/accepted postings (docs/Bounty-Hunting.md, Phase F).
        public static string Write(Bounty.BountyBoardData d, bool pretty = false)
        {
            d = d ?? new Bounty.BountyBoardData();
            var w = new Writer(pretty);
            w.BeginObj();
            w.Key("postings"); w.BeginArr();
            foreach (var p in d.postings ?? new List<Bounty.BountyPosting>())
            {
                if (p == null) continue;
                w.BeginObj();
                w.Field("id", p.id);
                w.Field("tier", p.tier);
                w.Field("biome", p.biome);
                w.Field("x", p.x);
                w.Field("y", p.y);
                w.Field("z", p.z);
                w.Field("acceptedBy", p.acceptedBy);
                w.Field("acceptedByName", p.acceptedByName);
                w.Field("spawned", p.spawned);
                w.Field("postedTicks", p.postedTicks);
                w.Field("tutorial", p.tutorial);
                w.EndObj();
            }
            w.EndArr();
            w.EndObj();
            return w.ToString();
        }

        public static Bounty.BountyBoardData ReadBountyBoard(string json)
        {
            var root = Parse(json) as Dictionary<string, object>;
            var d = new Bounty.BountyBoardData();
            if (root == null) return d;
            foreach (var o in Arr(root, "postings"))
            {
                if (!(o is Dictionary<string, object> p)) continue;
                d.postings.Add(new Bounty.BountyPosting
                {
                    id = S(p, "id"),
                    tier = I(p, "tier", 1),
                    biome = I(p, "biome"),
                    x = F(p, "x"),
                    y = F(p, "y"),
                    z = F(p, "z"),
                    acceptedBy = L(p, "acceptedBy"),
                    acceptedByName = S(p, "acceptedByName"),
                    spawned = B(p, "spawned"),
                    postedTicks = L(p, "postedTicks"),
                    tutorial = B(p, "tutorial"),
                });
            }
            return d;
        }

        public static string Write(ChampionsData d, bool pretty = false)
        {
            d = d ?? new ChampionsData();
            var w = new Writer(pretty);
            w.BeginObj();
            w.Key("champions"); w.BeginArr();
            foreach (var c in d.champions ?? new List<ChampionRecord>())
            {
                w.BeginObj();
                w.Field("mode", c.mode);
                w.Field("championLabel", c.championLabel);
                w.Field("ownerName", c.ownerName);
                w.Field("bracketSize", c.bracketSize);
                w.Field("seasonId", c.seasonId);
                w.Field("dateTicks", c.dateTicks);
                w.EndObj();
            }
            w.EndArr();
            w.EndObj();
            return w.ToString();
        }

        private static void WriteCompanion(Writer w, CompanionRecord c)
        {
            w.BeginObj();
            w.Field("companionId", c.companionId);
            w.Field("ownerId", c.ownerId);
            w.Field("ownerName", c.ownerName);
            w.Field("companionName", c.companionName);
            w.Field("caste", c.caste);
            w.Field("wins", c.wins);
            w.Field("losses", c.losses);
            w.Field("winStreak", c.winStreak);
            w.Field("bestStreak", c.bestStreak);
            w.Field("rating", c.rating);
            w.Field("lastMatchTicks", c.lastMatchTicks);
            w.Field("seasonId", c.seasonId);
            w.EndObj();
        }

        private static void WriteParty(Writer w, PartyRecord p)
        {
            w.BeginObj();
            w.Field("ownerId", p.ownerId);
            w.Field("ownerName", p.ownerName);
            w.Field("partyName", p.partyName);
            w.Field("wins", p.wins);
            w.Field("losses", p.losses);
            w.Field("rating", p.rating);
            w.Field("bestTeamSize", p.bestTeamSize);
            w.Field("lastMatchTicks", p.lastMatchTicks);
            w.Field("seasonId", p.seasonId);
            w.Key("memberSnapshot"); w.BeginArr();
            foreach (var m in p.memberSnapshot ?? new List<PartyMemberSnap>())
            {
                w.BeginObj();
                w.Field("companionId", m.companionId);
                w.Field("caste", m.caste);
                w.Field("level", m.level);
                w.EndObj();
            }
            w.EndArr();
            w.EndObj();
        }

        private static void WriteEntrant(Writer w, TournamentEntrant e)
        {
            w.BeginObj();
            w.Field("entrantId", e.entrantId);
            w.Field("ownerId", e.ownerId);
            w.Field("ownerName", e.ownerName);
            w.Field("label", e.label);
            w.Field("caste", e.caste);
            w.Field("seedRating", e.seedRating);
            w.Field("eliminated", e.eliminated);
            w.Field("level", e.level);
            w.Field("losses", e.losses);
            w.Field("wins", e.wins);
            w.Field("totemPayload", e.totemPayload);
            w.Field("feePaid", e.feePaid);
            w.Key("teamPayloads"); w.BeginArr();
            foreach (var p in e.teamPayloads ?? new List<string>()) w.ArrString(p);
            w.EndArr();
            w.EndObj();
        }

        private static void WriteMatch(Writer w, TournamentMatch m)
        {
            w.BeginObj();
            w.Field("round", m.round);
            w.Field("aId", m.aId);
            w.Field("bId", m.bId);
            w.Field("aLabel", m.aLabel);
            w.Field("bLabel", m.bLabel);
            w.Field("aLevel", m.aLevel);
            w.Field("bLevel", m.bLevel);
            w.Field("winnerId", m.winnerId);
            w.Field("bracket", m.bracket);
            w.Field("aReady", m.aReady);
            w.Field("bReady", m.bReady);
            w.Field("activated", m.activated);
            w.EndObj();
        }

        // ---- readers ----------------------------------------------------------

        public static LeaderboardData ReadLeaderboard(string json)
        {
            var root = Parse(json) as Dictionary<string, object>;
            var d = new LeaderboardData();
            if (root == null) return d;
            d.seasonId = I(root, "seasonId", d.seasonId);
            foreach (var o in Arr(root, "companions"))
            {
                if (!(o is Dictionary<string, object> c)) continue;
                d.companions.Add(new CompanionRecord
                {
                    companionId = S(c, "companionId"),
                    ownerId = L(c, "ownerId"),
                    ownerName = S(c, "ownerName"),
                    companionName = S(c, "companionName"),
                    caste = I(c, "caste"),
                    wins = I(c, "wins"),
                    losses = I(c, "losses"),
                    winStreak = I(c, "winStreak"),
                    bestStreak = I(c, "bestStreak"),
                    rating = I(c, "rating", Rating.StartRating),
                    lastMatchTicks = L(c, "lastMatchTicks"),
                    seasonId = I(c, "seasonId", 1),
                });
            }
            foreach (var o in Arr(root, "parties"))
            {
                if (!(o is Dictionary<string, object> p)) continue;
                var rec = new PartyRecord
                {
                    ownerId = L(p, "ownerId"),
                    ownerName = S(p, "ownerName"),
                    partyName = S(p, "partyName"),
                    wins = I(p, "wins"),
                    losses = I(p, "losses"),
                    rating = I(p, "rating", Rating.StartRating),
                    bestTeamSize = I(p, "bestTeamSize"),
                    lastMatchTicks = L(p, "lastMatchTicks"),
                    seasonId = I(p, "seasonId", 1),
                };
                foreach (var mo in Arr(p, "memberSnapshot"))
                {
                    if (!(mo is Dictionary<string, object> m)) continue;
                    rec.memberSnapshot.Add(new PartyMemberSnap
                    {
                        companionId = S(m, "companionId"),
                        caste = I(m, "caste"),
                        level = I(m, "level"),
                    });
                }
                d.parties.Add(rec);
            }
            return d;
        }

        public static TournamentState ReadTournament(string json)
        {
            var root = Parse(json) as Dictionary<string, object>;
            return root == null ? new TournamentState() : ReadTournamentObj(root);
        }

        private static TournamentState ReadTournamentObj(Dictionary<string, object> root)
        {
            var s = new TournamentState();
            if (root == null) return s;
            s.key = S(root, "key");
            s.currency = S(root, "currency");
            s.entryFee = I(root, "entryFee");
            s.prize = I(root, "prize");
            s.hostId = L(root, "hostId");
            s.hostName = S(root, "hostName");
            s.openedTicks = L(root, "openedTicks");
            s.hostCredit = I(root, "hostCredit");
            s.completedTicks = L(root, "completedTicks");
            s.active = B(root, "active");
            s.mode = S(root, "mode", s.mode);
            s.phase = S(root, "phase", s.phase);
            s.size = I(root, "size");
            s.bracketSize = I(root, "bracketSize");
            s.currentRound = I(root, "currentRound");
            s.championId = S(root, "championId");
            s.championLabel = S(root, "championLabel");
            s.seasonId = I(root, "seasonId", 1);
            s.eliminationType = S(root, "eliminationType", s.eliminationType);
            s.totalRounds = I(root, "totalRounds");
            foreach (var o in Arr(root, "entrants"))
            {
                if (!(o is Dictionary<string, object> e)) continue;
                var ent = new TournamentEntrant
                {
                    entrantId = S(e, "entrantId"),
                    ownerId = L(e, "ownerId"),
                    ownerName = S(e, "ownerName"),
                    label = S(e, "label"),
                    caste = I(e, "caste"),
                    seedRating = I(e, "seedRating"),
                    eliminated = B(e, "eliminated"),
                    level = I(e, "level"),
                    losses = I(e, "losses"),
                    wins = I(e, "wins"),
                    totemPayload = S(e, "totemPayload"),
                    feePaid = I(e, "feePaid"),
                };
                foreach (var p in Arr(e, "teamPayloads"))
                    if (p is string ps) ent.teamPayloads.Add(ps);
                s.entrants.Add(ent);
            }
            foreach (var o in Arr(root, "matches"))
            {
                if (!(o is Dictionary<string, object> m)) continue;
                s.matches.Add(new TournamentMatch
                {
                    round = I(m, "round"),
                    aId = S(m, "aId"),
                    bId = S(m, "bId"),
                    aLabel = S(m, "aLabel"),
                    bLabel = S(m, "bLabel"),
                    aLevel = I(m, "aLevel"),
                    bLevel = I(m, "bLevel"),
                    winnerId = S(m, "winnerId"),
                    bracket = S(m, "bracket", "W"),
                    aReady = B(m, "aReady"),
                    bReady = B(m, "bReady"),
                    activated = B(m, "activated"),
                });
            }
            return s;
        }

        // Staked duel invites (docs/Wagers.md). Same hand-rolled treatment as
        // everything else here — this file is the single place that knows how the
        // competitive data is written, precisely because the engine serializer
        // cannot be trusted with list fields on this runtime.
        public static string Write(DuelInviteBoard b, bool pretty = false)
        {
            b = b ?? new DuelInviteBoard();
            var w = new Writer(pretty);
            w.BeginObj();
            w.Key("invites"); w.BeginArr();
            foreach (var i in b.invites ?? new List<DuelInvite>())
            {
                if (i == null) continue;
                w.BeginObj();
                w.Field("id", i.id);
                w.Field("currency", i.currency);
                w.Field("stake", i.stake);
                w.Field("phase", i.phase);
                w.Field("hostId", i.hostId);
                w.Field("hostName", i.hostName);
                w.Field("hostEntrantId", i.hostEntrantId);
                w.Field("hostLabel", i.hostLabel);
                w.Field("hostLevel", i.hostLevel);
                w.Field("hostCaste", i.hostCaste);
                w.Field("hostPayload", i.hostPayload);
                w.Field("hostReady", i.hostReady);
                w.Field("oppId", i.oppId);
                w.Field("oppName", i.oppName);
                w.Field("oppEntrantId", i.oppEntrantId);
                w.Field("oppLabel", i.oppLabel);
                w.Field("oppLevel", i.oppLevel);
                w.Field("oppCaste", i.oppCaste);
                w.Field("oppPayload", i.oppPayload);
                w.Field("oppReady", i.oppReady);
                w.Field("createdTicks", i.createdTicks);
                w.Field("completedTicks", i.completedTicks);
                w.EndObj();
            }
            w.EndArr();
            w.EndObj();
            return w.ToString();
        }

        public static DuelInviteBoard ReadInvites(string json)
        {
            var board = new DuelInviteBoard();
            var root = Parse(json) as Dictionary<string, object>;
            if (root == null) return board;
            foreach (var o in Arr(root, "invites"))
            {
                if (!(o is Dictionary<string, object> i)) continue;
                board.invites.Add(new DuelInvite
                {
                    id = S(i, "id"),
                    currency = S(i, "currency"),
                    stake = I(i, "stake"),
                    phase = S(i, "phase", "open"),
                    hostId = L(i, "hostId"),
                    hostName = S(i, "hostName"),
                    hostEntrantId = S(i, "hostEntrantId"),
                    hostLabel = S(i, "hostLabel"),
                    hostLevel = I(i, "hostLevel"),
                    hostCaste = I(i, "hostCaste"),
                    hostPayload = S(i, "hostPayload"),
                    hostReady = B(i, "hostReady"),
                    oppId = L(i, "oppId"),
                    oppName = S(i, "oppName"),
                    oppEntrantId = S(i, "oppEntrantId"),
                    oppLabel = S(i, "oppLabel"),
                    oppLevel = I(i, "oppLevel"),
                    oppCaste = I(i, "oppCaste"),
                    oppPayload = S(i, "oppPayload"),
                    oppReady = B(i, "oppReady"),
                    createdTicks = L(i, "createdTicks"),
                    completedTicks = L(i, "completedTicks"),
                });
            }
            return board;
        }

        public static ChampionsData ReadChampions(string json)
        {
            var root = Parse(json) as Dictionary<string, object>;
            var d = new ChampionsData();
            if (root == null) return d;
            foreach (var o in Arr(root, "champions"))
            {
                if (!(o is Dictionary<string, object> c)) continue;
                d.champions.Add(new ChampionRecord
                {
                    mode = S(c, "mode"),
                    championLabel = S(c, "championLabel"),
                    ownerName = S(c, "ownerName"),
                    bracketSize = I(c, "bracketSize"),
                    seasonId = I(c, "seasonId", 1),
                    dateTicks = L(c, "dateTicks"),
                });
            }
            return d;
        }

        // ---- typed accessors --------------------------------------------------

        private static string S(Dictionary<string, object> d, string k, string def = "")
            => d.TryGetValue(k, out var v) && v is string s ? s : def;

        private static int I(Dictionary<string, object> d, string k, int def = 0)
            => d.TryGetValue(k, out var v) ? (v is long l ? (int)l : v is double f ? (int)f : def) : def;

        private static long L(Dictionary<string, object> d, string k, long def = 0L)
            => d.TryGetValue(k, out var v) ? (v is long l ? l : v is double f ? (long)f : def) : def;

        private static bool B(Dictionary<string, object> d, string k, bool def = false)
            => d.TryGetValue(k, out var v) && v is bool b ? b : def;

        // World coordinates (bounty postings) are the only non-integer values stored.
        private static float F(Dictionary<string, object> d, string k, float def = 0f)
            => d.TryGetValue(k, out var v) ? (v is double f ? (float)f : v is long l ? l : def) : def;

        private static List<object> Arr(Dictionary<string, object> d, string k)
            => d.TryGetValue(k, out var v) && v is List<object> a ? a : new List<object>();

        // ---- writer -----------------------------------------------------------

        private class Writer
        {
            private readonly StringBuilder _sb = new StringBuilder(256);
            private readonly bool _pretty;
            private int _depth;
            private bool _needComma;

            public Writer(bool pretty) { _pretty = pretty; }

            public void BeginObj() { Sep(); _sb.Append('{'); _depth++; _needComma = false; }
            public void EndObj() { _depth--; NewlineIndent(); _sb.Append('}'); _needComma = true; }
            public void BeginArr() { _sb.Append('['); _depth++; _needComma = false; }
            public void EndArr() { _depth--; NewlineIndent(); _sb.Append(']'); _needComma = true; }

            public void Key(string k)
            {
                Sep();
                _sb.Append('"').Append(k).Append("\":");
                if (_pretty) _sb.Append(' ');
                _needComma = false;
            }

            public void Field(string k, string v) { Key(k); WriteString(v); _needComma = true; }
            public void Field(string k, int v) { Key(k); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); _needComma = true; }
            public void Field(string k, long v) { Key(k); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); _needComma = true; }
            public void Field(string k, bool v) { Key(k); _sb.Append(v ? "true" : "false"); _needComma = true; }
            // "R" round-trips exactly, and InvariantCulture is essential: on a
            // comma-decimal locale the default would emit 12,5 and split the value
            // across the JSON comma, silently corrupting every coordinate.
            public void Field(string k, float v) { Key(k); _sb.Append(v.ToString("R", CultureInfo.InvariantCulture)); _needComma = true; }
            public void ArrString(string v) { Sep(); WriteString(v); _needComma = true; }

            private void Sep()
            {
                if (_needComma) _sb.Append(',');
                NewlineIndent();
            }

            private void NewlineIndent()
            {
                if (!_pretty || _depth == 0) return;
                _sb.Append('\n').Append(' ', _depth * 4);
            }

            private void WriteString(string v)
            {
                _sb.Append('"');
                foreach (var c in v ?? string.Empty)
                {
                    switch (c)
                    {
                        case '"': _sb.Append("\\\""); break;
                        case '\\': _sb.Append("\\\\"); break;
                        case '\n': _sb.Append("\\n"); break;
                        case '\r': _sb.Append("\\r"); break;
                        case '\t': _sb.Append("\\t"); break;
                        default:
                            if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else _sb.Append(c);
                            break;
                    }
                }
                _sb.Append('"');
            }

            public override string ToString() => _sb.ToString();
        }

        // ---- parser -----------------------------------------------------------

        // Minimal recursive-descent JSON reader: objects → Dictionary<string,object>,
        // arrays → List<object>, numbers → long (integral) or double, plus
        // string/bool/null. Returns null on malformed input (caller falls back to
        // a fresh data object).
        private static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int i = 0;
            try
            {
                var v = ParseValue(json, ref i);
                return v;
            }
            catch { return null; }
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't') { Expect(s, ref i, "true"); return true; }
            if (c == 'f') { Expect(s, ref i, "false"); return false; }
            if (c == 'n') { Expect(s, ref i, "null"); return null; }
            return ParseNumber(s, ref i);
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; // '{'
            SkipWs(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                var key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (s[i] != ':') throw new System.FormatException("expected ':'");
                i++;
                d[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new System.FormatException("expected ',' or '}'");
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var a = new List<object>();
            i++; // '['
            SkipWs(s, ref i);
            if (s[i] == ']') { i++; return a; }
            while (true)
            {
                a.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return a; }
                throw new System.FormatException("expected ',' or ']'");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new System.FormatException("expected '\"'");
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw new System.FormatException("bad escape");
                    }
                }
                else sb.Append(c);
            }
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            var raw = s.Substring(start, i - start);
            if (raw.IndexOf('.') < 0 && raw.IndexOf('e') < 0 && raw.IndexOf('E') < 0
                && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                return l;
            return double.Parse(raw, CultureInfo.InvariantCulture);
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new System.FormatException($"expected '{word}'");
            i += word.Length;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }
    }
}
