using LostScrollsII.Integration;
using UnityEngine;

namespace LostScrollsII.Companions
{
    // Implements the Communion Rite recruit flow from docs/Ally-Recruitment.md:
    // subdue a Dvergr below a health threshold, then perform Communion to flip
    // it to a player-allied companion. Phase 2 MVP: Rogue only, no Sword of
    // Truth item gate yet (deferred — see docs/Ally-Recruitment.md open questions).
    public static class CommunionService
    {
        // Fraction of max health a Dvergr must be at or below before Communion can succeed.
        public const float SubdueHealthThreshold = 0.2f;

        // Detects a Dvergr's caste. The mage element is NOT in the creature's
        // GameObject name (the spawned creature is just "DvergerMage") — it's
        // carried by the equipped STAFF (DvergerStaffFire / ...Ice / ...Support /
        // ...Heal / ...Nova / ...Blocker), verified against the asset strings. So
        // we classify by the staff the creature actually has (equipped weapon,
        // then its inventory), and only fall back to the GameObject name in case
        // some spawns use distinct fire/ice/support prefabs. Anything with no
        // staff is the melee Rogue. The signals are logged so a miss is
        // diagnosable from the BepInEx log rather than re-guessed.
        // `log` is false for the per-frame hover path (HoverTextPatch), which would
        // otherwise spam the BepInEx log every render frame the crosshair sits on a
        // Dvergr. The recruit path keeps logging so a mis-detect stays diagnosable.
        public static DvergrCaste DetectCaste(Character target, bool log = true)
        {
            if (target == null) return DvergrCaste.Rogue;

            var humanoid = target.GetComponent<Humanoid>();
            if (humanoid != null)
            {
                // 1. Weapon slots — drawn AND sheathed. A Dvergr that isn't
                //    mid-attack sheaths its staff, so it sits in the *hidden*
                //    slot and GetCurrentWeapon() returns null. Check all of them.
                foreach (var slotName in new[]
                {
                    ItemName(humanoid.GetCurrentWeapon()),
                    ItemName(humanoid.m_rightItem),
                    ItemName(humanoid.m_hiddenRightItem),
                    ItemName(humanoid.m_leftItem),
                    ItemName(humanoid.m_hiddenLeftItem),
                })
                {
                    var c = ClassifyByElement(slotName);
                    if (c.HasValue)
                    {
                        if (log) Plugin.Log.LogInfo($"[recruit] caste {c.Value} from weapon slot '{slotName}'.");
                        return c.Value;
                    }
                }

                // 2. Anything staff-like in its inventory (per-instance — not the
                //    prefab's random pool — so it can't mis-read a multi-staff pool).
                var inv = humanoid.GetInventory();
                if (inv != null)
                {
                    foreach (var item in inv.GetAllItems())
                    {
                        var name = ItemName(item);
                        var c = (name != null && name.ToLowerInvariant().Contains("staff"))
                            ? ClassifyByElement(name) : null;
                        if (c.HasValue)
                        {
                            if (log) Plugin.Log.LogInfo($"[recruit] caste {c.Value} from inventory item '{name}'.");
                            return c.Value;
                        }
                    }
                }
            }

            // 3. Fallback: distinct fire/ice/support creature prefabs, if any.
            var prefab = target.name;
            if (!string.IsNullOrEmpty(prefab))
            {
                if (prefab.IndexOf("Fire", System.StringComparison.OrdinalIgnoreCase) >= 0) return DvergrCaste.FireMage;
                if (prefab.IndexOf("Ice", System.StringComparison.OrdinalIgnoreCase) >= 0) return DvergrCaste.IceMage;
                if (prefab.IndexOf("Support", System.StringComparison.OrdinalIgnoreCase) >= 0) return DvergrCaste.SupportMage;
            }

            // 4. No staff found — treat as Rogue, but log every signal so a
            //    mis-detected mage can be corrected from the log.
            if (log) LogDetectionMiss(target, humanoid);
            return DvergrCaste.Rogue;
        }

        // Maps an element keyword in a weapon/staff name to a mage caste; null if
        // none. The dverger staves don't overlap (Fire / Ice / Support|Heal|Nova|
        // Blocker|Shield), so first-match order is safe.
        private static DvergrCaste? ClassifyByElement(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var n = name.ToLowerInvariant();
            if (n.Contains("fire")) return DvergrCaste.FireMage;
            if (n.Contains("ice") || n.Contains("frost")) return DvergrCaste.IceMage;
            if (n.Contains("support") || n.Contains("heal") || n.Contains("nova")
                || n.Contains("blocker") || n.Contains("shield")) return DvergrCaste.SupportMage;
            return null;
        }

        private static string ItemName(ItemDrop.ItemData item)
        {
            if (item == null) return null;
            if (item.m_dropPrefab != null) return item.m_dropPrefab.name;
            return item.m_shared != null ? item.m_shared.m_name : null;
        }

        private static void LogDetectionMiss(Character target, Humanoid humanoid)
        {
            string Slots()
            {
                if (humanoid == null) return "(no humanoid)";
                return $"cur='{ItemName(humanoid.GetCurrentWeapon())}', right='{ItemName(humanoid.m_rightItem)}', " +
                    $"hiddenRight='{ItemName(humanoid.m_hiddenRightItem)}', left='{ItemName(humanoid.m_leftItem)}', " +
                    $"hiddenLeft='{ItemName(humanoid.m_hiddenLeftItem)}'";
            }

            var items = "(none)";
            if (humanoid != null && humanoid.GetInventory() != null)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var item in humanoid.GetInventory().GetAllItems()) names.Add(ItemName(item));
                if (names.Count > 0) items = string.Join(", ", names);
            }

            Plugin.Log.LogInfo($"[recruit] No staff detected -> Rogue. prefab='{target.name}', " +
                $"slots[{Slots()}], inventory=[{items}].");
        }

        // Admin/debug: spawn an already-recruited companion of a caste at a
        // position, owned by `owner`, at an optional level. Used by the de_spawn
        // console command. Writes the companion ZDO flags before the behavior
        // component reads them, so caste/level/owner are picked up on Awake.
        public static GameObject SpawnRecruited(DvergrCaste caste, int level, Player owner, Vector3 pos, float xp = 0f)
        {
            if (ZNetScene.instance == null) return null;

            var prefab = ZNetScene.instance.GetPrefab(CastePrefab(caste));
            if (prefab == null && caste != DvergrCaste.Rogue) prefab = ZNetScene.instance.GetPrefab("DvergerMage");
            if (prefab == null) prefab = ZNetScene.instance.GetPrefab("Dverger");
            if (prefab == null) return null;

            var go = Object.Instantiate(prefab, pos, Quaternion.identity);
            var character = go.GetComponent<Character>();
            var znv = go.GetComponent<ZNetView>();
            if (znv != null && znv.IsValid())
            {
                var zdo = znv.GetZDO();
                zdo.Set(DvergrCompanion.ZdoKeyRecruited, true);
                zdo.Set(DvergrCompanion.ZdoKeyCaste, (int)caste);
                zdo.Set(DvergrCompanion.ZdoKeyLevel, Mathf.Clamp(level, 1, DvergrCompanion.MaxLevel));
                zdo.Set(DvergrCompanion.ZdoKeyXp, Mathf.Max(0f, xp));
                if (owner != null)
                {
                    zdo.Set(DvergrCompanion.ZdoKeyOwner, owner.GetPlayerID());
                    zdo.Set(DvergrCompanion.ZdoKeyOwnerName, owner.GetPlayerName());
                }
            }

            if (character != null) ApplyFreedState(character, owner != null ? owner.gameObject : null);
            if (go.GetComponent<DvergrCompanion>() == null) go.AddComponent<DvergrCompanion>();
            if (go.GetComponent<ShipRideAI>() == null) go.AddComponent<ShipRideAI>();

            Plugin.Log.LogInfo($"[admin] Spawned recruited {caste} (lv {level}) for {(owner != null ? owner.GetPlayerName() : "?")}.");
            return go;
        }

        private static string CastePrefab(DvergrCaste caste)
        {
            switch (caste)
            {
                case DvergrCaste.FireMage: return "DvergerMageFire";
                case DvergrCaste.IceMage: return "DvergerMageIce";
                case DvergrCaste.SupportMage: return "DvergerMageSupport";
                default: return "Dverger";
            }
        }

        public static bool IsSubduedDvergr(Character target)
        {
            if (target == null) return false;
            if (target.m_faction != Character.Faction.Dverger) return false;
            if (target.IsDead()) return false;
            return target.GetHealthPercentage() <= SubdueHealthThreshold;
        }

        public static bool TryRecruit(Character target, Player recruiter, DvergrCaste caste)
        {
            if (!IsSubduedDvergr(target)) return false;

            ApplyFreedState(target, recruiter != null ? recruiter.gameObject : null);

            var companion = target.GetComponent<DvergrCompanion>();
            if (companion == null)
            {
                companion = target.gameObject.AddComponent<DvergrCompanion>();
            }
            if (target.GetComponent<ShipRideAI>() == null) target.gameObject.AddComponent<ShipRideAI>();
            companion.SetCaste(caste);
            if (recruiter != null)
            {
                companion.SetOwner(recruiter.GetPlayerID());
                companion.SetOwnerName(recruiter.GetPlayerName());
            }

            var znv = target.GetComponent<ZNetView>();
            if (znv != null && znv.IsValid())
            {
                znv.GetZDO().Set(DvergrCompanion.ZdoKeyRecruited, true);
            }

            Plugin.Log.LogInfo($"Dvergr {caste} recruited by {(recruiter != null ? recruiter.GetPlayerName() : "unknown")}.");
            ServerGuideBridge.RaiseRecruited(caste);

            // Replace the (now-silenced) vanilla Dvergr chatter with a line about
            // what this freed ally can do in its current (Follow) stance.
            companion.AnnounceCapability();

            return true;
        }

        // Rebuilds companion state when a recruited Dvergr re-spawns (relog,
        // server restart, chunk reload). Called from CompanionRestorePatch on
        // MonsterAI.Start. ROOT CAUSE of "communed Dvergr reverts to uncommuned
        // after logging out/in": recruitment only changed runtime state
        // (m_faction, the AI's aggravated flag) and added the DvergrCompanion
        // component — none of which vanilla persists. The prefab respawns as a
        // plain hostile Dvergr, so its hover shows [G] Communion again. The only
        // thing that DID persist is our own DE_Recruited ZDO flag, so that flag
        // is the source of truth: on every Dvergr spawn we re-read it and
        // reconstruct the freed companion.
        public static void RestoreCompanion(Character target)
        {
            if (target == null) return;

            var znv = target.GetComponent<ZNetView>();
            if (znv == null || !znv.IsValid()) return;
            if (!znv.GetZDO().GetBool(DvergrCompanion.ZdoKeyRecruited)) return;

            ApplyFreedState(target, null);

            // Re-attach the behavior component; its Awake reads caste/level/xp
            // back out of the ZDO (which vanilla does persist for us).
            if (target.GetComponent<DvergrCompanion>() == null)
            {
                target.gameObject.AddComponent<DvergrCompanion>();
            }
            if (target.GetComponent<ShipRideAI>() == null)
            {
                target.gameObject.AddComponent<ShipRideAI>();
            }

            // Resume a persisted chore (survives relog / zone reload). ChoreAI's
            // Awake reads the chore kind + target position and re-issues it once the
            // target's zone has loaded. Without this the freed ally would respawn
            // idle at the owner's side instead of back at its station.
            if (ChoreAI.HasPersistedChore(znv) && target.GetComponent<ChoreAI>() == null)
            {
                target.gameObject.AddComponent<ChoreAI>();
            }
        }

        // Shared "this Dvergr is now a freed ally" AI state, used by both the
        // live recruit and the on-load restore.
        private static void ApplyFreedState(Character target, GameObject followTarget)
        {
            target.m_faction = Character.Faction.Players;

            // Silence the vanilla Dvergr chatter (NpcTalk drives the random
            // "intruder!"/grumble lines, greets, goodbyes and aggravated barks).
            // A freed ally shouldn't keep talking like a hostile camp Dvergr — we
            // replace it with our own stance/caste capability lines (see
            // DvergrCompanion.AnnounceCapability).
            var talk = target.GetComponent<NpcTalk>();
            if (talk != null) talk.enabled = false;

            var ai = target.GetComponent<MonsterAI>();
            if (ai == null) return;

            // ROOT CAUSE of "communed Dvergr still attacks the player,
            // including mid-fight" (verified by decompiling the real assembly,
            // not guessing): Dvergr are *neutral until aggravated*. Attacking
            // one calls SetAggravated(true) and it is that m_aggravated flag —
            // NOT faction — that drives its hostility. So a faction flip (and
            // even vanilla MonsterAI.MakeTame, which only does SetTamed +
            // SetAlerted(false) + clears the target) leaves m_aggravated set,
            // and the freed Dvergr keeps hunting whoever aggravated it. Clearing
            // it is the actual fix. AggravatedReason.Damage is the reason
            // vanilla uses when the aggravation came from being hit.
            ai.SetAggravated(false, BaseAI.AggravatedReason.Damage);

            // Then make the freed ally PERMANENTLY non-aggravatable. Root cause of
            // "attacking a wild Dvergr near my companion makes the companion attack
            // ME": when any Dvergr is hit it calls BaseAI.AggravateAllInArea, which
            // re-aggravates every nearby AI whose m_aggravatable flag is set — and
            // that flag is a prefab property still true on our recruited Dvergr. Once
            // re-aggravated, the "neutral Dvergr turns hostile to players" behavior
            // targets the owner. Clearing m_aggravatable removes the ally from that
            // area-aggravation sweep entirely (and SetAggravated(true) becomes a
            // no-op for it), so a freed ally can never revert to the hostile state.
            // MUST run AFTER SetAggravated(false) above — that call early-outs once
            // m_aggravatable is false, so flipping it first would skip the clear.
            // It still fights wild Dvergr normally via enemy targeting; only the
            // aggravation pathway is severed. Re-applied on every restore (this
            // method runs on recruit and on spawn-restore alike).
            ai.m_aggravatable = false;

            // Stop a freed ally from attacking the player's BUILD PIECES. Dvergr
            // spawn with MonsterAI.m_attackPlayerObjects = true, which makes their AI
            // seek StaticTarget structures (walls, workbench, etc.) as valid targets.
            // Faction-flipping to Players doesn't clear it, so a recruited Dvergr
            // would happily smash your base. Turn it off, and drop any structure it's
            // already locked onto (m_targetStatic) so it stops immediately.
            ai.m_attackPlayerObjects = false;
            ai.m_targetStatic = null;

            // Keep a freed ally out of the water by default — it shouldn't wander
            // into the sea and drown/drift. ShipRideAI clears this only while the
            // owner is aboard a ship, so the ally may swim out to board and stay
            // with it (see docs/Ship-Riding.md). Baseline set here so it holds even
            // for companions ShipRideAI isn't currently driving (chore/guard/etc.).
            ai.m_avoidWater = true;

            // Stop any current engagement immediately: drop the target (the
            // proper public method, which also clears m_targetStatic), and clear
            // alerted/hunt so it doesn't re-acquire on the next tick.
            ai.SetTarget(null);
            ai.SetAlerted(false);
            ai.SetHuntPlayer(false);

            if (followTarget != null)
            {
                ai.SetFollowTarget(followTarget);
            }
        }
    }
}
