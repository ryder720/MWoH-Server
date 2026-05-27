using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MwohServer.Data;
using MwohServer.Models;

namespace MwohServer.Services
{
    public class BattleEngine : IBattleEngine
    {
        private readonly ILogger<BattleEngine> _logger;
        private readonly MwohDbContext _dbContext;
        private readonly ICardAbilityEvaluator _abilityEvaluator;
        private readonly IAllianceEngine _allianceEngine;
        private readonly ISpecialComboEngine _specialComboEngine;

        public BattleEngine(
            ILogger<BattleEngine> logger,
            MwohDbContext dbContext,
            ICardAbilityEvaluator abilityEvaluator,
            IAllianceEngine allianceEngine,
            ISpecialComboEngine specialComboEngine)
        {
            _logger = logger;
            _dbContext = dbContext;
            _abilityEvaluator = abilityEvaluator;
            _allianceEngine = allianceEngine;
            _specialComboEngine = specialComboEngine;
        }

        public void RestoreBattlePower(PlayerProfile profile)
        {
            if (profile == null) return;

            var now = DateTime.UtcNow;
            var lastRecovery = DateTime.SpecifyKind(profile.LastBattlePowerRecoveryTime, DateTimeKind.Utc);

            var attackInterval = GameplaySettings.AttackRecoveryIntervalSeconds > 0 ? GameplaySettings.AttackRecoveryIntervalSeconds : 180;
            var attackAmount = GameplaySettings.AttackRecoveryAmount > 0 ? GameplaySettings.AttackRecoveryAmount : 1;
            
            var defenseInterval = GameplaySettings.DefenseRecoveryIntervalSeconds > 0 ? GameplaySettings.DefenseRecoveryIntervalSeconds : 180;
            var defenseAmount = GameplaySettings.DefenseRecoveryAmount > 0 ? GameplaySettings.DefenseRecoveryAmount : 1;

            bool isAttackBelowMax = profile.AttackPowerCurrent < profile.AttackPower;
            bool isDefenseBelowMax = profile.DefensePowerCurrent < profile.DefensePower;

            if (isAttackBelowMax || isDefenseBelowMax)
            {
                var secondsElapsed = (now - lastRecovery).TotalSeconds;

                var attackIntervals = isAttackBelowMax ? (int)(secondsElapsed / attackInterval) : 0;
                var defenseIntervals = isDefenseBelowMax ? (int)(secondsElapsed / defenseInterval) : 0;

                if (attackIntervals > 0 || defenseIntervals > 0)
                {
                    if (attackIntervals > 0)
                    {
                        profile.AttackPowerCurrent = Math.Min(profile.AttackPower, profile.AttackPowerCurrent + attackIntervals * attackAmount);
                    }
                    if (defenseIntervals > 0)
                    {
                        profile.DefensePowerCurrent = Math.Min(profile.DefensePower, profile.DefensePowerCurrent + defenseIntervals * defenseAmount);
                    }

                    var secondsToAdvance = 0;
                    if (isAttackBelowMax && isDefenseBelowMax)
                    {
                        secondsToAdvance = Math.Max(attackIntervals * attackInterval, defenseIntervals * defenseInterval);
                    }
                    else if (isAttackBelowMax)
                    {
                        secondsToAdvance = attackIntervals * attackInterval;
                    }
                    else
                    {
                        secondsToAdvance = defenseIntervals * defenseInterval;
                    }

                    if (secondsToAdvance > 0)
                    {
                        profile.LastBattlePowerRecoveryTime = lastRecovery.AddSeconds(secondsToAdvance);
                        _dbContext.SaveChanges();
                    }
                }
            }
            else
            {
                if (lastRecovery < now)
                {
                    profile.LastBattlePowerRecoveryTime = now;
                    _dbContext.SaveChanges();
                }
            }
        }

        public BattleResolutionResult ResolveBattle(int attackerProfileId, int defenderProfileId, bool isSparring)
        {
            var result = new BattleResolutionResult();
            var log = result.LogLines;

            if (attackerProfileId == defenderProfileId)
            {
                result.Success = false;
                result.Message = "You cannot engage in combat with yourself.";
                return result;
            }

            // 1. Load profiles with decks & alliances
            var attacker = _dbContext.Profiles
                .Include(p => p.Alliance)
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate)
                .FirstOrDefault(p => p.Id == attackerProfileId);

            var defender = _dbContext.Profiles
                .Include(p => p.Alliance)
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate)
                .FirstOrDefault(p => p.Id == defenderProfileId);

            if (attacker == null || defender == null)
            {
                result.Success = false;
                result.Message = "Attacker or Defender profile not found.";
                return result;
            }

            // Lazy Restore Battle Power for both players
            RestoreBattlePower(attacker);
            RestoreBattlePower(defender);

            // 2. Guards: 3x daily attack limit per target
            var today = DateTime.UtcNow.Date;
            var attackCountToday = _dbContext.BattleRecords
                .Count(r => r.AttackerProfileId == attackerProfileId 
                            && r.DefenderProfileId == defenderProfileId 
                            && r.BattleTime >= today);

            if (attackCountToday >= 3)
            {
                result.Success = false;
                result.Message = $"Classified block: You have reached your daily limit of 3 attacks against Agent {defender.Nickname}.";
                return result;
            }

            // 3. Assemble Attack Deck & Defense Deck
            var attackerCards = attacker.Cards.Where(c => c.IsInAttackDeck).ToList();
            if (!attackerCards.Any())
            {
                var leader = attacker.Cards.FirstOrDefault(c => c.IsLeader);
                if (leader != null) attackerCards.Add(leader);
                else
                {
                    var topCard = attacker.Cards.OrderByDescending(c => c.CurrentAtk).FirstOrDefault();
                    if (topCard != null) attackerCards.Add(topCard);
                }
            }

            var defenderCards = defender.Cards.Where(c => c.IsInDefenseDeck).ToList();
            if (!defenderCards.Any())
            {
                var leader = defender.Cards.FirstOrDefault(c => c.IsLeader);
                if (leader != null) defenderCards.Add(leader);
                else
                {
                    var topCard = defender.Cards.OrderByDescending(c => c.CurrentDef).FirstOrDefault();
                    if (topCard != null) defenderCards.Add(topCard);
                }
            }

            if (!attackerCards.Any() || !defenderCards.Any())
            {
                result.Success = false;
                result.Message = "Combat cannot be resolved without valid cards in squads.";
                return result;
            }

            // 4. Power / cost verification
            var attackerCost = attackerCards.Sum(c => c.CardTemplate?.PowerRequirement ?? 0);
            var defenderCost = defenderCards.Sum(c => c.CardTemplate?.PowerRequirement ?? 0);

            if (attacker.AttackPowerCurrent < attackerCost)
            {
                result.Success = false;
                result.Message = $"Insufficient Clearance Power. Required: {attackerCost} ATK Power, Available: {attacker.AttackPowerCurrent}.";
                return result;
            }

            // Capture states before
            result.AttackerAttackPowerBefore = attacker.AttackPowerCurrent;
            result.AttackerAttackPowerMax = attacker.AttackPower;
            result.DefenderDefensePowerBefore = defender.DefensePowerCurrent;
            result.DefenderDefensePowerMax = defender.DefensePower;

            log.Add($"[SYSTEM LOG] Battle initiated. Mode: {(isSparring ? "Sparring Match (No Resource Transfers)" : "Ranked S.H.I.E.L.D. Combat")}");
            log.Add($"[AGENT DOSSIER] Attacker: {attacker.Nickname} (Lvl {attacker.Level}) vs Defender: {defender.Nickname} (Lvl {defender.Level})");
            log.Add($"[DEPLOYMENT] Attacker squad deployed. Clearance Cost: {attackerCost} / {attacker.AttackPower}");
            log.Add($"[DEPLOYMENT] Defender squad deployed. Clearance Cost: {defenderCost} / {defender.DefensePower}");

            // Apply scaling to defender stats if they have low defense power
            double defenderScale = 1.0;
            if (defender.DefensePowerCurrent < defenderCost && defenderCost > 0)
            {
                defenderScale = (double)defender.DefensePowerCurrent / defenderCost;
                log.Add($"[WARNING] Defender defense points are depleted ({defender.DefensePowerCurrent}/{defenderCost}). Card effectiveness scaled to {Math.Round(defenderScale * 100)}%!");
            }

            // 5. Abilities trigger check: 35% roll, max 3 triggers per deck
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
                        attackerAbilityLogs.Add($"[ABILITY TRIGGER] {card.CardTemplate.VisualTitle} activated {card.CardTemplate.AbilityName}! Effect: {card.CardTemplate.AbilityEffect} (Lvl {card.AbilityLevel})");
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
                        defenderAbilityLogs.Add($"[ABILITY TRIGGER] {card.CardTemplate.VisualTitle} activated {card.CardTemplate.AbilityName}! Effect: {card.CardTemplate.AbilityEffect} (Lvl {card.AbilityLevel})");
                    }
                }
            }

            // Apply S.H.I.E.L.D. Alliance boosts (adaptors, role bonuses, walls)
            var attackerBoosts = new List<string>();
            var defenderBoosts = new List<string>();

            // Create evaluation models with ability name cleared for non-triggered ones and apply Alliance boosts
            var attackerEvalCards = attackerCards.Select(c => {
                var alignment = c.CardTemplate?.Alignment ?? "Speed";
                var boost = _allianceEngine.GetAllianceCombatBoosts(attackerProfileId, alignment);

                int boostedAtk = (int)Math.Round(c.CurrentAtk * boost.AtkModifier);
                int boostedDef = (int)Math.Round(c.CurrentDef * boost.DefModifier);

                if (!string.IsNullOrEmpty(boost.Logs))
                {
                    foreach (var line in boost.Logs.Split('\n'))
                    {
                        if (!attackerBoosts.Contains(line)) attackerBoosts.Add(line);
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
                        VisualTitle = c.CardTemplate.VisualTitle,
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

            var defenderEvalCards = defenderCards.Select(c => {
                var alignment = c.CardTemplate?.Alignment ?? "Speed";
                var boost = _allianceEngine.GetAllianceCombatBoosts(defenderProfileId, alignment);

                int boostedAtk = (int)Math.Round(c.CurrentAtk * defenderScale * boost.AtkModifier);
                int boostedDef = (int)Math.Round(c.CurrentDef * defenderScale * boost.DefModifier);

                if (!string.IsNullOrEmpty(boost.Logs))
                {
                    foreach (var line in boost.Logs.Split('\n'))
                    {
                        if (!defenderBoosts.Contains(line)) defenderBoosts.Add(line);
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
                        VisualTitle = c.CardTemplate.VisualTitle,
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

            // 6. Evaluate final combat stats
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
                            var charName = SpecialComboEngine.GetCharacterName(stat.Card.CardTemplate?.Title);
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

            bool attackerWon = finalAttackerPower > finalDefenderPower;
            result.AttackerWon = attackerWon;

            // 7. Process Silver Transfer
            long silverTransferred = 0;
            if (!isSparring)
            {
                if (attackerWon)
                {
                    // Winner takes 10% of defender's silver balance (capped at 10,500)
                    silverTransferred = (long)Math.Floor(defender.SilverBalance * 0.10);
                    if (silverTransferred > 10500) silverTransferred = 10500;
                    if (silverTransferred < 0) silverTransferred = 0;

                    attacker.SilverBalance += silverTransferred;
                    defender.SilverBalance = Math.Max(0, defender.SilverBalance - silverTransferred);
                    result.SilverExchanged = silverTransferred;
                    log.Add($"[REWARD TRANSFER] Attacker Victorious! Gained {silverTransferred} Silver from Agent {defender.Nickname}.");
                }
                else
                {
                    log.Add($"[TACTICAL FAILURE] Attacker Defeated! Defender successfully secured their database resources.");
                }
            }
            else
            {
                log.Add("[SPARRING RESOLUTION] Combat ended. No silver or resources exchanged during tactical sparring.");
            }

            // 8. Process Card Mastery gains
            int masteryGained = 0;
            if (attackerWon)
            {
                // Level-proportional Mastery points boost to cards in the Attack Deck
                if (defender.Level < attacker.Level)
                {
                    masteryGained = random.Next(0, 2); // 0 or +1
                }
                else if (defender.Level == attacker.Level)
                {
                    masteryGained = 3;
                }
                else // defender.Level > attacker.Level
                {
                    masteryGained = random.Next(4, 6); // +4 or +5
                }

                result.MasteryEarned = masteryGained;

                if (masteryGained > 0)
                {
                    foreach (var card in attackerCards)
                    {
                        if (card.CardTemplate != null)
                        {
                            var maxMastery = card.CardTemplate.MaxMastery > 0 ? card.CardTemplate.MaxMastery : 100;
                            var beforeMastery = card.CurrentMastery;
                            card.CurrentMastery = Math.Min(maxMastery, card.CurrentMastery + masteryGained);
                            card.RecalculateStats();
                            log.Add($"[MASTERY UPDATE] {card.CardTemplate.VisualTitle} Mastery increased by +{masteryGained} ({beforeMastery} -> {card.CurrentMastery}). Stats recalculated!");
                        }
                    }
                }
            }

            // 9. Point depletion
            attacker.AttackPowerCurrent = Math.Max(0, attacker.AttackPowerCurrent - attackerCost);
            
            var defPointsDeducted = Math.Max(5, (int)Math.Ceiling(defenderCost * 0.10));
            defender.DefensePowerCurrent = Math.Max(0, defender.DefensePowerCurrent - defPointsDeducted);

            result.AttackerAttackPowerAfter = attacker.AttackPowerCurrent;
            result.DefenderDefensePowerAfter = defender.DefensePowerCurrent;

            log.Add($"[DEPLTEION] Attacker ATK points: {result.AttackerAttackPowerBefore} -> {result.AttackerAttackPowerAfter} (Used {attackerCost})");
            log.Add($"[DEPLETION] Defender DEF points: {result.DefenderDefensePowerBefore} -> {result.DefenderDefensePowerAfter} (Weakened by {defPointsDeducted})");

            // 10. Record Battle Log Record
            var record = new BattleRecord
            {
                AttackerProfileId = attackerProfileId,
                DefenderProfileId = defenderProfileId,
                WinnerProfileId = attackerWon ? attackerProfileId : defenderProfileId,
                AttackerFinalPower = finalAttackerPower,
                DefenderFinalPower = finalDefenderPower,
                SilverExchanged = silverTransferred,
                MasteryEarned = masteryGained,
                BattleTime = DateTime.UtcNow,
                IsSparring = isSparring,
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(log)
            };

            _dbContext.BattleRecords.Add(record);
            _dbContext.SaveChanges();

            result.Success = true;
            result.Message = attackerWon ? "Victory!" : "Defeat!";

            return result;
        }
    }
}
