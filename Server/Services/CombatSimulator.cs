using MwohServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MwohServer.Services
{
    public class CombatSimulator : ICombatSimulator
    {
        private readonly ICardAbilityEvaluator _abilityEvaluator;
        private readonly ISpecialComboEngine _specialComboEngine;

        public CombatSimulator(ICardAbilityEvaluator abilityEvaluator, ISpecialComboEngine specialComboEngine)
        {
            _abilityEvaluator = abilityEvaluator;
            _specialComboEngine = specialComboEngine;
        }

        public CombatSimulationResult Simulate(
            List<PlayerCard> attackerCards, 
            List<PlayerCard> defenderCards,
            string attackerNickname,
            int attackerLevel,
            int attackerPower,
            int attackerCost,
            string defenderNickname,
            int defenderLevel,
            int defenderPower,
            int defenderCost,
            double defenderScale,
            List<AllianceStatsBoost> attackerAllianceBoosts,
            List<AllianceStatsBoost> defenderAllianceBoosts,
            bool isSparring)
        {
            var result = new CombatSimulationResult();
            var log = result.LogLines;

            log.Add($"[SYSTEM LOG] Battle initiated. Mode: {(isSparring ? "Sparring Match (No Resource Transfers)" : "Ranked S.H.I.E.L.D. Combat")}");
            log.Add($"[AGENT DOSSIER] Attacker: {attackerNickname} (Lvl {attackerLevel}) vs Defender: {defenderNickname} (Lvl {defenderLevel})");
            log.Add($"[DEPLOYMENT] Attacker squad deployed. Clearance Cost: {attackerCost} / {attackerPower}");
            log.Add($"[DEPLOYMENT] Defender squad deployed. Clearance Cost: {defenderCost} / {defenderPower}");

            if (defenderScale < 1.0 && defenderCost > 0)
            {
                log.Add($"[WARNING] Defender defense points are depleted. Card effectiveness scaled to {Math.Round(defenderScale * 100)}%!");
            }

            // 1. Abilities trigger check: 35% roll, max 3 triggers per deck
            var random = new Random();
            var triggeredAttackerCardIds = new HashSet<int>();
            var attackerAbilityLogs = new List<string>();
            var attackerTriggerCount = 0;

            foreach (var card in attackerCards)
            {
                if (card.CardTemplate != null && !string.IsNullOrEmpty(card.CardTemplate.AbilityName))
                {
                    if (attackerTriggerCount < 3 && random.NextDouble() < 0.35)
                    {
                        triggeredAttackerCardIds.Add(card.Id);
                        attackerTriggerCount++;
                        attackerAbilityLogs.Add($"[ABILITY TRIGGER] {card.GetDisplayName()} activated {card.CardTemplate.AbilityName}! Effect: {card.CardTemplate.AbilityEffect} (Lvl {card.AbilityLevel})");
                    }
                }
            }

            var triggeredDefenderCardIds = new HashSet<int>();
            var defenderAbilityLogs = new List<string>();
            var defenderTriggerCount = 0;

            foreach (var card in defenderCards)
            {
                if (card.CardTemplate != null && !string.IsNullOrEmpty(card.CardTemplate.AbilityName))
                {
                    if (defenderTriggerCount < 3 && random.NextDouble() < 0.35)
                    {
                        triggeredDefenderCardIds.Add(card.Id);
                        defenderTriggerCount++;
                        defenderAbilityLogs.Add($"[ABILITY TRIGGER] {card.GetDisplayName()} activated {card.CardTemplate.AbilityName}! Effect: {card.CardTemplate.AbilityEffect} (Lvl {card.AbilityLevel})");
                    }
                }
            }

            result.AttackerTriggerCount = attackerTriggerCount;
            result.DefenderTriggerCount = defenderTriggerCount;

            // Apply S.H.I.E.L.D. Alliance boosts (adaptors, role bonuses, walls)
            var attackerBoosts = new List<string>();
            var defenderBoosts = new List<string>();

            // Create evaluation models with ability name cleared for non-triggered ones and apply Alliance boosts
            var attackerEvalCards = attackerCards.Select((c, idx) => {
                var boost = attackerAllianceBoosts[idx];

                int boostedAtk = (int)Math.Round(c.CurrentAtk * boost.AtkModifier);
                int boostedDef = (int)Math.Round(c.CurrentDef * boost.DefModifier);

                if (!string.IsNullOrEmpty(boost.Logs))
                {
                    foreach (var line in boost.Logs.Split('\n'))
                    {
                        if (!string.IsNullOrWhiteSpace(line) && !attackerBoosts.Contains(line)) attackerBoosts.Add(line);
                    }
                }

                return new PlayerCard
                {
                    Id = c.Id,
                    CurrentAtk = boostedAtk,
                    CurrentDef = boostedDef,
                    AbilityLevel = c.AbilityLevel,
                    CardTemplate = new CardTemplate
                    {
                        Id = c.CardTemplate!.Id,
                        Title = c.CardTemplate.Title,
                        VisualTitle = c.GetDisplayName(),
                        Alignment = c.CardTemplate.Alignment,
                        Rarity = c.CardTemplate.Rarity,
                        Faction = c.CardTemplate.Faction,
                        Gender = c.CardTemplate.Gender,
                        PowerRequirement = c.CardTemplate.PowerRequirement,
                        BaseAtk = (int)Math.Round(c.CardTemplate.BaseAtk * boost.AtkModifier),
                        BaseDef = (int)Math.Round(c.CardTemplate.BaseDef * boost.DefModifier),
                        MaxAtk = (int)Math.Round(c.CardTemplate.MaxAtk * boost.AtkModifier),
                        MaxDef = (int)Math.Round(c.CardTemplate.MaxDef * boost.DefModifier),
                        MasteryBonusAtk = (int)Math.Round(c.CardTemplate.MasteryBonusAtk * boost.AtkModifier),
                        MasteryBonusDef = (int)Math.Round(c.CardTemplate.MasteryBonusDef * boost.DefModifier),
                        MaxMastery = c.CardTemplate.MaxMastery,
                        AbilityName = triggeredAttackerCardIds.Contains(c.Id) ? c.CardTemplate.AbilityName : "",
                        AbilityEffect = triggeredAttackerCardIds.Contains(c.Id) ? c.CardTemplate.AbilityEffect : "",
                        Quote = c.CardTemplate.Quote,
                        ImageFileName = c.CardTemplate.ImageFileName,
                        VariantName = c.CardTemplate.VariantName
                    }
                };
            }).ToList();

            var defenderEvalCards = defenderCards.Select((c, idx) => {
                var boost = defenderAllianceBoosts[idx];

                int boostedAtk = (int)Math.Round(c.CurrentAtk * defenderScale * boost.AtkModifier);
                int boostedDef = (int)Math.Round(c.CurrentDef * defenderScale * boost.DefModifier);

                if (!string.IsNullOrEmpty(boost.Logs))
                {
                    foreach (var line in boost.Logs.Split('\n'))
                    {
                        if (!string.IsNullOrWhiteSpace(line) && !defenderBoosts.Contains(line)) defenderBoosts.Add(line);
                    }
                }

                return new PlayerCard
                {
                    Id = c.Id,
                    CurrentAtk = boostedAtk,
                    CurrentDef = boostedDef,
                    AbilityLevel = c.AbilityLevel,
                    CardTemplate = new CardTemplate
                    {
                        Id = c.CardTemplate!.Id,
                        Title = c.CardTemplate.Title,
                        VisualTitle = c.GetDisplayName(),
                        Alignment = c.CardTemplate.Alignment,
                        Rarity = c.CardTemplate.Rarity,
                        Faction = c.CardTemplate.Faction,
                        Gender = c.CardTemplate.Gender,
                        PowerRequirement = c.CardTemplate.PowerRequirement,
                        BaseAtk = (int)Math.Round(c.CardTemplate.BaseAtk * defenderScale * boost.AtkModifier),
                        BaseDef = (int)Math.Round(c.CardTemplate.BaseDef * defenderScale * boost.DefModifier),
                        MaxAtk = (int)Math.Round(c.CardTemplate.MaxAtk * defenderScale * boost.AtkModifier),
                        MaxDef = (int)Math.Round(c.CardTemplate.MaxDef * defenderScale * boost.DefModifier),
                        MasteryBonusAtk = (int)Math.Round(c.CardTemplate.MasteryBonusAtk * defenderScale * boost.AtkModifier),
                        MasteryBonusDef = (int)Math.Round(c.CardTemplate.MasteryBonusDef * defenderScale * boost.DefModifier),
                        MaxMastery = c.CardTemplate.MaxMastery,
                        AbilityName = triggeredDefenderCardIds.Contains(c.Id) ? c.CardTemplate.AbilityName : "",
                        AbilityEffect = triggeredDefenderCardIds.Contains(c.Id) ? c.CardTemplate.AbilityEffect : "",
                        Quote = c.CardTemplate.Quote,
                        ImageFileName = c.CardTemplate.ImageFileName,
                        VariantName = c.CardTemplate.VariantName
                    }
                };
            }).ToList();

            // Evaluate final combat stats
            var attackerCombatStats = _abilityEvaluator.EvaluateDeck(attackerEvalCards, defenderEvalCards, isAttackingDeck: true);
            var defenderCombatStats = _abilityEvaluator.EvaluateDeck(defenderEvalCards, attackerEvalCards, isAttackingDeck: false);

            // Process S.H.I.E.L.D. Special Combos
            var attackerCombos = _specialComboEngine.ProcessDeckCombos(attackerCards, isAttacking: true);
            var defenderCombos = _specialComboEngine.ProcessDeckCombos(defenderCards, isAttacking: false);

            // Helper to apply Special Combo buffs/debuffs to deck combat stats
            void ApplyComboEffect(SpecialComboResult combo, List<PlayerCardCombatStats> friendlyStats, List<PlayerCardCombatStats> opposingStats)
            {
                var targets = combo.Target == ComboTarget.Friendly ? friendlyStats : opposingStats;
                
                foreach (var stat in targets)
                {
                    bool inScope = false;
                    switch (combo.Scope)
                    {
                        case ComboScope.All:
                            inScope = true;
                            break;
                        case ComboScope.Alignment:
                            inScope = combo.ScopeDetail.Any(d => string.Equals(stat.Card.CardTemplate?.Alignment, d, StringComparison.OrdinalIgnoreCase));
                            break;
                        case ComboScope.Faction:
                            inScope = combo.ScopeDetail.Any(d => string.Equals(stat.Card.CardTemplate?.Faction, d, StringComparison.OrdinalIgnoreCase));
                            if (!inScope && combo.EffectText.ToLower().Contains("hero"))
                            {
                                    inScope = string.Equals(stat.Card.CardTemplate?.Faction, "Super Hero", StringComparison.OrdinalIgnoreCase);
                            }
                            if (!inScope && combo.EffectText.ToLower().Contains("villain"))
                            {
                                    inScope = string.Equals(stat.Card.CardTemplate?.Faction, "Villain", StringComparison.OrdinalIgnoreCase);
                            }
                            break;
                        case ComboScope.SpecificCharacters:
                            var charName = SpecialComboEngine.GetCharacterName(stat.Card.CardTemplate?.Title ?? "");
                            inScope = combo.ScopeDetail.Any(d => string.Equals(charName, d, StringComparison.OrdinalIgnoreCase));
                            break;
                    }

                    if (inScope)
                    {
                        if (combo.AffectedStat == ComboStat.Atk || combo.AffectedStat == ComboStat.AtkDef)
                        {
                            if (combo.Target == ComboTarget.Friendly) stat.ActiveBuffPercentageAtk += combo.PowerValue;
                            else stat.ActiveDebuffPercentageAtk += combo.PowerValue;
                        }
                        if (combo.AffectedStat == ComboStat.Def || combo.AffectedStat == ComboStat.AtkDef)
                        {
                            if (combo.Target == ComboTarget.Friendly) stat.ActiveBuffPercentageDef += combo.PowerValue;
                            else stat.ActiveDebuffPercentageDef += combo.PowerValue;
                        }
                    }
                }
            }

            foreach (var combo in attackerCombos)
            {
                if (combo.Triggered)
                {
                    ApplyComboEffect(combo, attackerCombatStats, defenderCombatStats);
                }
                log.Add(combo.LogLine);
            }

            foreach (var combo in defenderCombos)
            {
                if (combo.Triggered)
                {
                    ApplyComboEffect(combo, defenderCombatStats, attackerCombatStats);
                }
                log.Add(combo.LogLine);
            }

            var finalAttackerPower = attackerCombatStats.Sum(s => s.FinalAtk);
            var finalDefenderPower = defenderCombatStats.Sum(s => s.FinalDef);

            result.AttackerFinalPower = finalAttackerPower;
            result.DefenderFinalPower = finalDefenderPower;

            log.Add("[COMBAT INITIATED] Tactical calculations in progress...");
            foreach (var l in attackerBoosts) log.Add(l);
            foreach (var l in defenderBoosts) log.Add(l);
            foreach (var l in attackerAbilityLogs) log.Add(l);
            foreach (var l in defenderAbilityLogs) log.Add(l);

            log.Add($"[STAT RESOLUTION] Attacker Final ATK Power: {finalAttackerPower}");
            log.Add($"[STAT RESOLUTION] Defender Final DEF Power: {finalDefenderPower}");

            result.AttackerWon = finalAttackerPower > finalDefenderPower;

            return result;
        }
    }
}
