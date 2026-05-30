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
        private readonly IAllianceEngine _allianceEngine;
        private readonly ICombatSimulator _combatSimulator;

        private readonly IAssignmentEngine? _assignmentEngine;

        public BattleEngine(
            ILogger<BattleEngine> logger,
            MwohDbContext dbContext,
            IAllianceEngine allianceEngine,
            ICombatSimulator combatSimulator,
            IAssignmentEngine? assignmentEngine = null)
        {
            _logger = logger;
            _dbContext = dbContext;
            _allianceEngine = allianceEngine;
            _combatSimulator = combatSimulator;
            _assignmentEngine = assignmentEngine;
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

            // S.H.I.E.L.D. Barrier raid protection logic
            if (!isSparring)
            {
                var barrierItem = _dbContext.PlayerInventoryItems
                    .Include(pi => pi.ItemTemplate)
                    .FirstOrDefault(pi => pi.PlayerProfileId == defenderProfileId && pi.ItemTemplate != null && pi.ItemTemplate.Name.Contains("Shield Barrier") && pi.Quantity > 0);

                if (barrierItem != null)
                {
                    barrierItem.Quantity--;
                    _dbContext.SaveChanges();

                    result.Success = true;
                    result.AttackerWon = false;
                    result.SilverExchanged = 0;
                    result.MasteryEarned = 0;
                    result.Message = "Blocked by S.H.I.E.L.D. Barrier!";

                    log.Add("[SHIELD ACTIVE] Tactical intrusion detected!");
                    log.Add($"[SHIELD ACTIVE] Defender Agent {defender.Nickname} is protected by an active S.H.I.E.L.D. Barrier!");
                    log.Add("[SHIELD ACTIVE] Incident blocked. No database records or Silver resources were compromised.");

                    var shieldRecord = new BattleRecord
                    {
                        AttackerProfileId = attackerProfileId,
                        DefenderProfileId = defenderProfileId,
                        WinnerProfileId = defenderProfileId,
                        AttackerFinalPower = 0,
                        DefenderFinalPower = 0,
                        SilverExchanged = 0,
                        MasteryEarned = 0,
                        BattleTime = DateTime.UtcNow,
                        IsSparring = isSparring,
                        DetailsJson = System.Text.Json.JsonSerializer.Serialize(log)
                    };

                    _dbContext.BattleRecords.Add(shieldRecord);
                    _dbContext.SaveChanges();

                    return result;
                }
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

            // Calculate defender scale if low defense power
            double defenderScale = 1.0;
            if (defender.DefensePowerCurrent < defenderCost && defenderCost > 0)
            {
                defenderScale = (double)defender.DefensePowerCurrent / defenderCost;
            }

            log.Add($"[SYSTEM LOG] Battle initiated. Mode: {(isSparring ? "Sparring Match (No Resource Transfers)" : "Ranked S.H.I.E.L.D. Combat")}");

            // Fetch aligned alliance boosts
            var attackerAllianceBoosts = attackerCards.Select(c => {
                var alignment = c.CardTemplate?.Alignment ?? "Speed";
                return _allianceEngine.GetAllianceCombatBoosts(attackerProfileId, alignment);
            }).ToList();

            var defenderAllianceBoosts = defenderCards.Select(c => {
                var alignment = c.CardTemplate?.Alignment ?? "Speed";
                return _allianceEngine.GetAllianceCombatBoosts(defenderProfileId, alignment);
            }).ToList();

            // Invoke pure Combat Simulator
            var simResult = _combatSimulator.Simulate(
                attackerCards,
                defenderCards,
                attacker.Nickname,
                attacker.Level,
                attacker.AttackPower,
                attackerCost,
                defender.Nickname,
                defender.Level,
                defender.DefensePower,
                defenderCost,
                defenderScale,
                attackerAllianceBoosts,
                defenderAllianceBoosts,
                isSparring);

            result.AttackerFinalPower = simResult.AttackerFinalPower;
            result.DefenderFinalPower = simResult.DefenderFinalPower;
            
            bool attackerWon = simResult.AttackerWon;
            result.AttackerWon = attackerWon;

            var attackerTriggerCount = simResult.AttackerTriggerCount;

            log.AddRange(simResult.LogLines);

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
            var random = new Random();
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
                AttackerFinalPower = simResult.AttackerFinalPower,
                DefenderFinalPower = simResult.DefenderFinalPower,
                SilverExchanged = silverTransferred,
                MasteryEarned = masteryGained,
                BattleTime = DateTime.UtcNow,
                IsSparring = isSparring,
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(log)
            };

            _dbContext.BattleRecords.Add(record);
            _dbContext.SaveChanges();

            // S.H.I.E.L.D. Assignment Hooks
            try
            {
                _assignmentEngine?.RecordEvent(attackerProfileId, GoalType.PvpBattle, 1);
                if (attackerWon)
                {
                    _assignmentEngine?.RecordEvent(attackerProfileId, GoalType.PvpWin, 1);
                    
                    if (attackerTriggerCount > 0)
                    {
                        _assignmentEngine?.RecordEvent(attackerProfileId, GoalType.SkillsActivated, 1);
                    }
                    
                    var hasMorale = attacker.AllianceId != null || attackerCards.GroupBy(c => c.CardTemplate?.Alignment).Any(g => g.Count() >= 3);
                    if (hasMorale)
                    {
                        _assignmentEngine?.RecordEvent(attackerProfileId, GoalType.MoraleWin, 1);
                    }

                    // Win Streak Calculation
                    var lastBattles = _dbContext.BattleRecords
                        .Where(r => r.AttackerProfileId == attackerProfileId && !r.IsSparring)
                        .OrderByDescending(r => r.BattleTime)
                        .Take(10)
                        .ToList();

                    int currentStreak = 0;
                    foreach (var b in lastBattles)
                    {
                        if (b.WinnerProfileId == attackerProfileId)
                        {
                            currentStreak++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (currentStreak > 0)
                    {
                        _assignmentEngine?.RecordEvent(attackerProfileId, GoalType.WinStreak, currentStreak);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[BattleEngine] Failed to record assignment progress: {ex.Message}");
            }

            result.Success = true;
            result.Message = attackerWon ? "Victory!" : "Defeat!";

            return result;
        }
    }
}
