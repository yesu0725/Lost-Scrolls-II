using System;
using HarmonyLib;
using LostScrollsII.Companions;

namespace LostScrollsII.Patches
{
    // Makes a recruited companion selectively hostile to *players* without
    // touching faction (which is all-or-nothing). BaseAI.IsEnemy is the single
    // chokepoint vanilla AI uses to decide who to target/attack, so injecting
    // here means the normal targeting/acquisition then "just works":
    //   - Guard stance  -> every non-owner player is an enemy.
    //   - Follow stance  -> only players the owner attacked (timed).
    //   - Any stance     -> a player who attacked us (retaliation, timed).
    //   - Feral (butcher)-> every player, owner included.
    // The owner is never an enemy (unless feral). See DvergrCompanion.IsHostileTo.
    //
    // Duel mode is handled first and authoritatively: a companion in duel mode
    // is an enemy ONLY to another player's duel-mode companion, and a non-enemy
    // to everything else (players, creatures, same-owner allies). This both
    // makes duelists seek each other and keeps them from attacking — or being
    // attacked by — anyone else for the duration (reqs 1 & 2).
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), new[] { typeof(Character), typeof(Character) })]
    public static class CompanionIsEnemyPatch
    {
        public static void Postfix(Character a, Character b, ref bool __result)
        {
            if (a == null || b == null) return;

            var ca = a.GetComponent<DvergrCompanion>();
            var cb = b.GetComponent<DvergrCompanion>();

            // Either duel mode (1v1 or party) overrides the normal relationship
            // entirely. A competitive duelist is an enemy ONLY to another player's
            // duelist in the SAME mode; a non-enemy (ignored + immune) to everyone
            // and everything else. Same-mode + different non-zero owners is the
            // single rule that makes 1v1 rivals and party teams find each other
            // while ignoring players, creatures, and same-owner allies.
            if ((ca != null && ca.InAnyDuelMode) || (cb != null && cb.InAnyDuelMode))
            {
                __result =
                    ca != null && cb != null &&
                    ca.OwnerId != 0L && cb.OwnerId != 0L &&
                    ca.OwnerId != cb.OwnerId &&
                    ((ca.DuelMode && cb.DuelMode) || (ca.PartyDuelMode && cb.PartyDuelMode));
                return;
            }

            if (__result) return;
            if (Hostile(a, b) || Hostile(b, a)) __result = true;
        }

        private static bool Hostile(Character maybeCompanion, Character maybeTarget)
        {
            if (maybeTarget == null) return false;
            var comp = maybeCompanion.GetComponent<DvergrCompanion>();
            // IsHostileTo now handles any Character, so a companion can be hostile
            // to another player's companion (settings 1 & 2), not just to a player.
            return comp != null && comp.IsHostileTo(maybeTarget);
        }
    }

    // Central damage gate for companions. Runs before vanilla Character.Damage so
    // it can both cancel a hit (return false) and drive hostility state.
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    public static class CompanionDamagePatch
    {
        public static bool Prefix(Character __instance, HitData hit)
        {
            if (__instance == null || hit == null) return true;

            var attackerChar = hit.GetAttacker();
            var attackerPlayer = attackerChar as Player;
            var victimComp = __instance.GetComponent<DvergrCompanion>();

            if (victimComp != null)
            {
                // A companion in either duel mode (1v1 or party):
                if (victimComp.InAnyDuelMode)
                {
                    // req 6: players cannot damage a dueling companion, even with PvP on.
                    if (attackerPlayer != null) return false;

                    // Already resolved this bout (subdued in 1v1 / benched in party):
                    // swallow any further hits that land before the exit flag
                    // replicates or while HP regen has nudged the loser back above
                    // the floor. Without this the winner keeps swinging and the bout
                    // would resolve (and be announced / point-awarded) more than once.
                    // See DvergrCompanion._duelResolved.
                    if (victimComp.IsDuelResolved) return false;

                    // Companion-vs-companion: enforce the non-lethal floor. A blow
                    // that would drop the duelist to/below the subdue floor is
                    // capped, then resolved idempotently: 1v1 credits the striker the
                    // win and ends the bout; party benches this member and lets the
                    // match play on (winners are paid at their stand-down).
                    float floor = __instance.GetMaxHealth() * DvergrCompanion.SubdueFloorHealthFraction;
                    if (__instance.GetHealth() - hit.GetTotalDamage() <= floor)
                    {
                        __instance.SetHealth(floor);
                        if (victimComp.PartyDuelMode)
                        {
                            victimComp.ResolvePartySubdue();
                        }
                        else
                        {
                            var winner = attackerChar != null ? attackerChar.GetComponent<DvergrCompanion>() : null;
                            victimComp.ResolveSubdue(winner);
                        }
                        return false; // skip the lethal damage
                    }
                    return true; // sub-lethal duel hit lands normally
                }

                // Not dueling. Player-struck companions:
                if (attackerPlayer != null)
                {
                    // Non-duel change: a butcher-knife strike (by anyone, owner
                    // included) turns the companion feral against all players.
                    if (IsButcherKnife(attackerChar)) victimComp.GoFeral(attackerPlayer);
                    else if (!victimComp.IsOwner(attackerPlayer))
                    {
                        // Retaliate against the attacking player...
                        victimComp.Retaliate(attackerPlayer);
                        // ...and (setting 1) against that player's own companions too,
                        // so a struck ally fights the aggressor AND its companions.
                        victimComp.MarkOwnersCompanionsHostile(attackerPlayer.GetPlayerID());
                    }
                }
                return true;
            }

            // Victim is a player and another player attacked them.
            var victim = __instance as Player;
            if (victim != null && attackerPlayer != null && victim != attackerPlayer)
            {
                long attackerId = attackerPlayer.GetPlayerID();
                long victimId = victim.GetPlayerID();

                foreach (var comp in DvergrCompanion.All)
                {
                    if (comp == null) continue;

                    // Existing behavior: the ATTACKER's Follow companions join in on
                    // the player their owner is attacking.
                    if (comp.OwnerId == attackerId && comp.Stance == CompanionStance.Follow)
                        comp.MarkHostile(victim);

                    // Setting 2: the ATTACKED player's companions retaliate — turning
                    // on the aggressor AND the aggressor's companions. Only when PvP
                    // is actually in play (that's what let the hit land at all).
                    if (comp.OwnerId == victimId && victim.IsPVPEnabled() && attackerPlayer.IsPVPEnabled())
                    {
                        comp.MarkHostile(attackerPlayer);
                        comp.MarkOwnersCompanionsHostile(attackerId);
                    }
                }
            }
            return true;
        }

        // The butcher knife is the "KnifeButcher" prefab ($item_knife_butcher).
        // Match on the attacker's currently equipped weapon by prefab or shared
        // name (both contain "butcher").
        private static bool IsButcherKnife(Character attacker)
        {
            var humanoid = attacker as Humanoid;
            var weapon = humanoid != null ? humanoid.GetCurrentWeapon() : null;
            if (weapon == null) return false;

            var prefab = weapon.m_dropPrefab != null ? weapon.m_dropPrefab.name : null;
            var shared = weapon.m_shared != null ? weapon.m_shared.m_name : null;
            return (prefab != null && prefab.IndexOf("Butcher", StringComparison.OrdinalIgnoreCase) >= 0)
                || (shared != null && shared.IndexOf("butcher", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
