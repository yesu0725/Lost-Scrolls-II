using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using LostScrollsII.Companions;
using UnityEngine;

namespace LostScrollsII.Patches
{
    // Sealing companions into totems at the Incinerator (docs/Companion-Totems.md).
    //
    // Flow (mechanics as specified):
    //  1. The player sets companions to Follow and gathers them at the incinerator.
    //  2. They drop Wisps in and pull the lever. If BOTH following companions and
    //     Wisps are present, we take over the incineration.
    //  3. The conversion is 1:1 — one Wisp becomes one GoblinTotem per companion.
    //     N = min(wisps, following companions). Extra companions/wisps are left as
    //     they are (reqs 3 & 5).
    //  4. Timed to the vanilla "lighting" window: the takeover mirrors the vanilla
    //     lever animation + effects, waits the same 5–7s dramatic delay, and only
    //     then swaps Wisps for named totems and dissolves those companions.
    //
    // Hook point: Incinerator.OnIncinerate — the Switch callback that fires on the
    // activating player's client. Returning false skips vanilla entirely (no
    // RPC_RequestIncinerate, no vanilla coroutine) so our own dramatic flow owns
    // the whole event. When it's NOT a companion sealing (no wisps or no followers)
    // we return true and let the base game incinerate normally.
    //
    // MP note: runs on the activator using the same ClaimOwnership pattern as the
    // DE_Duel replication. Single-player is fully authoritative; MP needs the usual
    // two-client verification pass (see docs/Testing.md).
    [HarmonyPatch(typeof(Incinerator), "OnIncinerate")]
    public static class IncineratorConversionPatch
    {
        // How close a Follow-stance companion must be to the incinerator to be
        // swept up in the ritual.
        private const float GatherRange = 15f;

        public static bool Prefix(Incinerator __instance, Humanoid user, ref bool __result)
        {
            var player = user as Player;
            if (player == null || player != Player.m_localPlayer) return true; // only the local activator drives it

            if (__instance == null || __instance.m_nview == null || !__instance.m_nview.IsValid()) return true;
            if (!PrivateArea.CheckAccess(__instance.transform.position)) return true; // let vanilla show its refusal
            if (__instance.m_container == null) return true;

            var inv = __instance.m_container.GetInventory();
            if (inv == null) return true;

            string wispName = TotemConversionService.WispSharedName();
            int wispCount = string.IsNullOrEmpty(wispName) ? 0 : inv.CountItems(wispName);
            if (wispCount <= 0) return true; // no wisps -> normal incineration

            var followers = GatherFollowers(player, __instance.transform.position);
            if (followers.Count == 0) return true; // no companions to seal -> normal incineration

            // Take over. __result true so the Switch plays the player's use feedback.
            __result = true;
            if (Plugin.Instance != null)
                Plugin.Instance.StartCoroutine(SealRitual(__instance, player, wispName));
            return false;
        }

        // Companions this player owns, in Follow stance, free (no chore/duel), alive,
        // and near the incinerator — nearest first.
        private static List<DvergrCompanion> GatherFollowers(Player player, Vector3 origin)
        {
            var list = new List<DvergrCompanion>();
            foreach (var c in DvergrCompanion.All)
            {
                if (c == null || !c.IsOwner(player)) continue;
                if (c.Stance != CompanionStance.Follow || c.ChoreActive || c.DuelMode) continue;
                var ch = c.GetComponent<Character>();
                if (ch == null || ch.IsDead()) continue;
                if (Vector3.Distance(origin, c.transform.position) > GatherRange) continue;
                list.Add(c);
            }
            list.Sort((a, b) => Vector3.Distance(origin, a.transform.position)
                .CompareTo(Vector3.Distance(origin, b.transform.position)));
            return list;
        }

        private static IEnumerator SealRitual(Incinerator inc, Player player, string wispName)
        {
            var nview = inc.m_nview;
            nview.ClaimOwnership();
            var containerNview = inc.m_container.GetComponent<ZNetView>();
            if (containerNview != null && containerNview.IsValid()) containerNview.ClaimOwnership();

            // Mirror the vanilla lever animation + lever effects, then hold for the
            // same dramatic window the base game uses before it resolves.
            nview.InvokeRPC(ZNetView.Everybody, "RPC_AnimateLever");
            inc.m_leverEffects.Create(inc.transform.position, inc.transform.rotation);

            yield return new WaitForSeconds(Random.Range(inc.m_effectDelayMin, inc.m_effectDelayMax));

            nview.InvokeRPC(ZNetView.Everybody, "RPC_AnimateLeverReturn");
            if (inc.m_lightingAOEs != null)
                Object.Instantiate(inc.m_lightingAOEs, inc.transform.position, inc.transform.rotation);

            // Re-evaluate live state (a companion may have died/wandered, or wisps
            // been taken, during the 5–7s window).
            var inv = inc.m_container.GetInventory();
            if (inv == null) yield break;
            int wispCount = string.IsNullOrEmpty(wispName) ? 0 : inv.CountItems(wispName);
            var followers = GatherFollowers(player, inc.transform.position);

            int n = Mathf.Min(wispCount, followers.Count);
            int sealed_ = 0;
            for (int i = 0; i < n; i++)
            {
                var companion = followers[i];
                if (companion == null) continue;

                var totem = TotemConversionService.CreateTotem(companion);
                if (totem == null) break; // prefab missing — nothing we can do

                if (!inv.AddItem(totem))
                {
                    // Container full: stop; leave the rest as they are (req 5).
                    player.Message(MessageHud.MessageType.Center, "The incinerator has no room for more totems.");
                    break;
                }

                inv.RemoveItem(wispName, 1);
                TotemConversionService.PlaySealVfx(companion.transform.position + Vector3.up);
                DissolveCompanion(companion);
                sealed_++;
            }

            if (sealed_ > 0)
                player.Message(MessageHud.MessageType.Center,
                    sealed_ == 1 ? "A companion is sealed within a totem." : $"{sealed_} companions are sealed within totems.");
        }

        // Removes the companion from the world (its state now lives on the totem).
        private static void DissolveCompanion(DvergrCompanion companion)
        {
            var go = companion.gameObject;
            var znv = go.GetComponent<ZNetView>();
            if (znv != null && znv.IsValid())
            {
                znv.ClaimOwnership();
                ZNetScene.instance.Destroy(go);
            }
            else
            {
                Object.Destroy(go);
            }
        }
    }
}
