using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MwohServer.Data;
using MwohServer.Models;

namespace MwohServer.Services
{
    public class WarEventEngine : IWarEventEngine
    {
        private readonly MwohDbContext _dbContext;
        private readonly ILogger<WarEventEngine> _logger;
        private readonly ICardAbilityEvaluator _abilityEvaluator;
        private readonly IAllianceEngine _allianceEngine;
        private readonly IEventEngine _eventEngine;

        public WarEventEngine(
            MwohDbContext dbContext,
            ILogger<WarEventEngine> logger,
            ICardAbilityEvaluator abilityEvaluator,
            IAllianceEngine allianceEngine,
            IEventEngine eventEngine)
        {
            _dbContext = dbContext;
            _logger = logger;
            _abilityEvaluator = abilityEvaluator;
            _allianceEngine = allianceEngine;
            _eventEngine = eventEngine;
        }

        public AllianceWarBattle? GetActiveWarBattle(int allianceId, string eventId)
        {
            return _dbContext.AllianceWarBattles
                .FirstOrDefault(b => b.EventId == eventId && b.Status == "Active" && (b.AllianceAId == allianceId || b.AllianceBId == allianceId));
        }

        public void EnterMatchmakingQueue(int allianceId)
        {
            var alliance = _dbContext.Alliances.Find(allianceId);
            if (alliance == null) return;

            alliance.IsQueuedForWar = true;
            alliance.WarQueueJoinedAt = DateTime.UtcNow;
            _dbContext.SaveChanges();
            _logger.LogInformation($"[WarEventEngine] Alliance '{alliance.Name}' ({allianceId}) entered matchmaking queue.");
        }

        public AllianceWarBattle? CheckOrMatchmakeAlliance(int allianceId, string eventId)
        {
            // If already in an active battle, return it
            var activeBattle = GetActiveWarBattle(allianceId, eventId);
            if (activeBattle != null) return activeBattle;

            var alliance = _dbContext.Alliances
                .Include(a => a.Members)
                .FirstOrDefault(a => a.Id == allianceId);
            if (alliance == null || !alliance.IsQueuedForWar) return null;

            // Look for another queued player Alliance
            var opponent = _dbContext.Alliances
                .Include(a => a.Members)
                .FirstOrDefault(a => a.Id != allianceId && a.IsQueuedForWar);

            if (opponent != null)
            {
                _logger.LogInformation($"[WarEventEngine] Matching player Alliance '{alliance.Name}' against rival player Alliance '{opponent.Name}'!");
                return InitializeBattle(alliance, opponent, eventId, isAi: false);
            }

            // Check if 10 minutes have elapsed for AI fallback
            if (alliance.WarQueueJoinedAt.HasValue && (DateTime.UtcNow - alliance.WarQueueJoinedAt.Value).TotalMinutes >= 10)
            {
                _logger.LogInformation($"[WarEventEngine] Matchmaking timeout (10 minutes) reached for '{alliance.Name}'. Deploying Mock AI Alliance opponent...");
                var aiAlliance = CreateMockAiAlliance(alliance);
                return InitializeBattle(alliance, aiAlliance, eventId, isAi: true);
            }

            return null;
        }

        private Alliance CreateMockAiAlliance(Alliance playerAlliance)
        {
            var rng = new Random();
            var aiNames = new[] { "HYDRA Vanguard Cell", "A.I.M. Mechanized Cohort", "Sentinel Strike Sector", "Shadow Syndicate", "Kree Empire Vanguard", "Skrull Infiltration Fleet" };
            var selectedName = aiNames[rng.Next(aiNames.Length)] + " #" + rng.Next(100, 999);

            var aiAlliance = new Alliance
            {
                Name = selectedName,
                Slogan = "Compliance is mandatory. Science will prevail!",
                Level = Math.Max(1, playerAlliance.Level + rng.Next(-1, 2)),
                Rating = playerAlliance.Rating + rng.Next(-20, 21),
                DonatedSilver = playerAlliance.DonatedSilver,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Alliances.Add(aiAlliance);
            _dbContext.SaveChanges();
            return aiAlliance;
        }

        private AllianceWarBattle InitializeBattle(Alliance allianceA, Alliance allianceB, string eventId, bool isAi)
        {
            // Formula: Health = 10,000 + Rating * 5
            long hpA = 10000 + allianceA.Rating * 5;
            long hpB = 10000 + allianceB.Rating * 5;

            var battle = new AllianceWarBattle
            {
                EventId = eventId,
                AllianceAId = allianceA.Id,
                AllianceBId = allianceB.Id,
                AllianceAHealthCurrent = hpA,
                AllianceAHealthMax = hpA,
                AllianceBHealthCurrent = hpB,
                AllianceBHealthMax = hpB,
                AllianceAValorCurrent = 0,
                AllianceBValorCurrent = 0,
                Status = "Active",
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddHours(2), // default 2 hour matches
                IsAiOpponent = isAi
            };

            battle.AllianceADefensiveLeadersJson = JsonSerializer.Serialize(SelectDefensiveLeaders(allianceA.Id, isAi: false));
            battle.AllianceBDefensiveLeadersJson = JsonSerializer.Serialize(SelectDefensiveLeaders(allianceB.Id, isAi));

            // Dequeue alliances
            allianceA.IsQueuedForWar = false;
            allianceA.WarQueueJoinedAt = null;
            
            allianceB.IsQueuedForWar = false;
            allianceB.WarQueueJoinedAt = null;

            _dbContext.AllianceWarBattles.Add(battle);
            _dbContext.SaveChanges();

            _logger.LogInformation($"[WarEventEngine] Battle initialized between Alliance A ({allianceA.Name}) and Alliance B ({allianceB.Name})! Core HP: {hpA} vs {hpB}. AI: {isAi}");
            return battle;
        }

        private List<WarDefensiveLeaderState> SelectDefensiveLeaders(int allianceId, bool isAi)
        {
            var leaders = new List<WarDefensiveLeaderState>();

            if (isAi)
            {
                var aiRoster = new[]
                {
                    new { Nick = "AI Custodian Prime", Role = "Leader", Card = 1001, CardName = "[Leopardess] Tigra", CardImg = "placeholder.jpg", Def = 150 },
                    new { Nick = "AI Strike Coordinator", Role = "Vice-Leader", Card = 1001, CardName = "[Precise Shot] Hawkeye", CardImg = "placeholder.jpg", Def = 120 },
                    new { Nick = "AI Bulwark Sentinel", Role = "Defense-Leader", Card = 1001, CardName = "[Advanced Intelligence] Ultron", CardImg = "placeholder.jpg", Def = 180 }
                };

                for (int i = 0; i < aiRoster.Length; i++)
                {
                    var unit = aiRoster[i];
                    leaders.Add(new WarDefensiveLeaderState
                    {
                        ProfileId = -(i + 1), // mock profile ids
                        Nickname = unit.Nick,
                        Role = unit.Role,
                        DefPowerMax = unit.Def,
                        DefPowerCurrent = unit.Def,
                        LeaderCardId = unit.Card,
                        LeaderCardTitle = unit.CardName,
                        LeaderCardImage = unit.CardImg
                    });
                }
                return leaders;
            }

            var members = _dbContext.Profiles
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate)
                .Where(p => p.AllianceId == allianceId)
                .ToList();

            // 1. Identify primary role assignments
            var assigned = members
                .Where(m => m.AllianceRole == "Leader" || m.AllianceRole == "Vice-Leader" || m.AllianceRole == "Defense-Leader")
                .OrderBy(m => m.AllianceRole == "Leader" ? 0 : m.AllianceRole == "Vice-Leader" ? 1 : 2)
                .ToList();

            foreach (var m in assigned)
            {
                var leaderCard = m.Cards.FirstOrDefault(c => c.IsLeader) 
                    ?? m.Cards.OrderByDescending(c => c.CurrentDef).FirstOrDefault();

                leaders.Add(new WarDefensiveLeaderState
                {
                    ProfileId = m.Id,
                    Nickname = m.Nickname,
                    Role = m.AllianceRole ?? "Member",
                    DefPowerMax = m.DefensePower,
                    DefPowerCurrent = m.DefensePower,
                    LeaderCardId = leaderCard?.Id ?? 0,
                    LeaderCardTitle = leaderCard?.CardTemplate?.Title ?? "Standard Vanguard",
                    LeaderCardImage = leaderCard?.CardTemplate?.ImageFileName ?? "placeholder.jpg"
                });
            }

            // 2. Fill remaining spots with highest Max DEF members
            if (leaders.Count < 3)
            {
                var remain = members
                    .Where(m => !leaders.Any(l => l.ProfileId == m.Id))
                    .OrderByDescending(m => m.DefensePower)
                    .ToList();

                foreach (var m in remain)
                {
                    if (leaders.Count >= 3) break;

                    var leaderCard = m.Cards.FirstOrDefault(c => c.IsLeader) 
                        ?? m.Cards.OrderByDescending(c => c.CurrentDef).FirstOrDefault();

                    leaders.Add(new WarDefensiveLeaderState
                    {
                        ProfileId = m.Id,
                        Nickname = m.Nickname,
                        Role = m.AllianceRole ?? "Member",
                        DefPowerMax = m.DefensePower,
                        DefPowerCurrent = m.DefensePower,
                        LeaderCardId = leaderCard?.Id ?? 0,
                        LeaderCardTitle = leaderCard?.CardTemplate?.Title ?? "Defensive Operative",
                        LeaderCardImage = leaderCard?.CardTemplate?.ImageFileName ?? "placeholder.jpg"
                    });
                }
            }

            // 3. Fallback: Add mock S.H.I.E.L.D. robots if total guild members < 3
            var botNames = new[] { "S.H.I.E.L.D. Custodian Bot", "S.H.I.E.L.D. Barrier Drone", "S.H.I.E.L.D. Iron Sentry" };
            int idOffset = 1000;
            while (leaders.Count < 3)
            {
                leaders.Add(new WarDefensiveLeaderState
                {
                    ProfileId = -(idOffset++),
                    Nickname = botNames[leaders.Count],
                    Role = "Defense-Leader",
                    DefPowerMax = 150,
                    DefPowerCurrent = 150,
                    LeaderCardId = 1001,
                    LeaderCardTitle = "Standard Sentry Grid",
                    LeaderCardImage = "placeholder.jpg"
                });
            }

            return leaders;
        }

        public WarBattleResolutionResult ResolveWarEngagement(int profileId, string eventId, int targetProfileId, bool isCoreAttack)
        {
            var result = new WarBattleResolutionResult();

            var profile = _dbContext.Profiles
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate)
                .Include(p => p.Alliance)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null || profile.AllianceId == null || profile.Alliance == null)
            {
                result.Message = "Strategic link broken: Profile or Alliance reference not found.";
                return result;
            }

            var allianceId = profile.AllianceId.Value;
            var battle = GetActiveWarBattle(allianceId, eventId);
            if (battle == null)
            {
                result.Message = "Strategic link conclustion: No active War Battle Matchup detected.";
                return result;
            }

            if (DateTime.UtcNow >= battle.EndTime)
            {
                ConcludeBattle(battle);
                result.Message = "Match conclude warning: Tactical deployment window expired.";
                return result;
            }

            // Determine role and active matchups
            bool isAllianceA = battle.AllianceAId == allianceId;
            var opposingLeadersJson = isAllianceA ? battle.AllianceBDefensiveLeadersJson : battle.AllianceADefensiveLeadersJson;
            var opposingLeaders = JsonSerializer.Deserialize<List<WarDefensiveLeaderState>>(opposingLeadersJson) ?? new();

            long opposingCoreHp = isAllianceA ? battle.AllianceBHealthCurrent : battle.AllianceAHealthCurrent;
            long opposingCoreHpMax = isAllianceA ? battle.AllianceBHealthMax : battle.AllianceAHealthMax;

            // Assemble Attack Deck
            var friendlyDeck = profile.Cards.Where(c => c.IsInAttackDeck).ToList();
            if (!friendlyDeck.Any())
            {
                var leader = profile.Cards.FirstOrDefault(c => c.IsLeader);
                if (leader != null) friendlyDeck.Add(leader);
                else
                {
                    var topCard = profile.Cards.OrderByDescending(c => c.CurrentAtk).FirstOrDefault();
                    if (topCard != null) friendlyDeck.Add(topCard);
                }
            }

            // AP check
            int apCost = friendlyDeck.Sum(c => c.CardTemplate?.PowerRequirement ?? 10);
            if (apCost <= 0) apCost = 10;

            if (profile.AttackPowerCurrent < apCost)
            {
                result.Message = $"Clearance reserve warnings: Insufficient Attack Power (Required: {apCost} AP).";
                return result;
            }

            // Deduct Attack Power
            profile.AttackPowerCurrent -= apCost;

            // Calculate Attacker Final ATK including Adaptors
            var friendlyStats = _abilityEvaluator.EvaluateDeck(friendlyDeck, new List<PlayerCard>(), isAttackingDeck: true);
            double totalFinalAtk = 0;
            foreach (var card in friendlyStats)
            {
                var alignment = card.Card.CardTemplate?.Alignment ?? "None";
                var boost = _allianceEngine.GetAllianceCombatBoosts(profileId, alignment);
                totalFinalAtk += card.FinalAtk * (1.0 + boost.AtkModifier);
            }

            result.CombatLogs.Add($"[DECK SQUAD INITIALIZATION] Deployed Attack Deck cost {apCost} AP.");
            result.CombatLogs.Add($"[TACTICAL CALCULATION] Base Deck final ATK with active research adaptors evaluated at {totalFinalAtk:N0} PTS.");

            if (isCoreAttack)
            {
                // Verify all defensive leaders are down
                var activeLeaders = opposingLeaders.Where(l => l.DefPowerCurrent > 0).ToList();
                if (activeLeaders.Any())
                {
                    result.Message = "Core Shields lock active: You must neutralize all rival defensive commanders before striking headquarters.";
                    // Refund AP for failed click in UI
                    profile.AttackPowerCurrent += apCost;
                    return result;
                }

                // Core Attack resolves directly
                long damage = (long)totalFinalAtk;
                long coreBefore = opposingCoreHp;
                long coreAfter = Math.Max(0, coreBefore - damage);

                if (isAllianceA)
                {
                    battle.AllianceBHealthCurrent = coreAfter;
                }
                else
                {
                    battle.AllianceAHealthCurrent = coreAfter;
                }

                int points = (int)(damage / 50);
                if (points <= 0) points = 5;

                result.Success = true;
                result.DamageDealt = damage;
                result.PointsEarned = points;
                result.OpponentName = isAllianceA ? "Rival Alliance Core B" : "Rival Alliance Core A";

                result.CombatLogs.Add($"[DIRECT CORE ASSAULT] Obliterated opposing HQ shields. Dealt {damage:N0} direct core damage.");
                result.CombatLogs.Add($"[HQ STATUS] Opponent headquarters Core Health reduced from {coreBefore:N0} to {coreAfter:N0}.");

                if (coreAfter == 0)
                {
                    // Victory! Conclude immediately
                    battle.WinnerAllianceId = allianceId;
                    battle.Status = "Concluded";
                    
                    int victoryBonus = 2000;
                    points += victoryBonus;
                    result.PointsEarned += victoryBonus;

                    result.CombatLogs.Add($"[TACTICAL SUPREMACY] Core fully obliterated! Alliance has won the battle, securing a massive +{victoryBonus} victory bonus!");
                }

                RecordEventPoints(profileId, eventId, points, isAllianceA, battle);
            }
            else
            {
                // Attack Defensive Leader
                var target = opposingLeaders.FirstOrDefault(l => l.ProfileId == targetProfileId);
                if (target == null || target.DefPowerCurrent <= 0)
                {
                    result.Message = "Opposing commander is already neutralized or not found in target banks.";
                    profile.AttackPowerCurrent += apCost;
                    return result;
                }

                double totalFinalDef = 0;
                if (targetProfileId > 0)
                {
                    // Real player defense deck
                    var targetProfile = _dbContext.Profiles
                        .Include(p => p.Cards)
                            .ThenInclude(c => c.CardTemplate)
                        .FirstOrDefault(p => p.Id == targetProfileId);

                    if (targetProfile != null)
                    {
                        var defenseDeck = targetProfile.Cards.Where(c => c.IsInDefenseDeck).ToList();
                        if (!defenseDeck.Any())
                        {
                            var leader = targetProfile.Cards.FirstOrDefault(c => c.IsLeader);
                            if (leader != null) defenseDeck.Add(leader);
                            else
                            {
                                var topCard = targetProfile.Cards.OrderByDescending(c => c.CurrentDef).FirstOrDefault();
                                if (topCard != null) defenseDeck.Add(topCard);
                            }
                        }

                        var targetStats = _abilityEvaluator.EvaluateDeck(new List<PlayerCard>(), defenseDeck, isAttackingDeck: false);
                        foreach (var card in targetStats)
                        {
                            var alignment = card.Card.CardTemplate?.Alignment ?? "None";
                            var boost = _allianceEngine.GetAllianceCombatBoosts(targetProfileId, alignment);
                            totalFinalDef += card.FinalDef * (1.0 + boost.DefModifier);
                        }
                    }
                }
                else
                {
                    // Simulated AI defense (Level scaled)
                    int scaleLevel = Math.Clamp(profile.Level, 1, 120);
                    totalFinalDef = scaleLevel * 150;
                }

                long netDamage = Math.Max((long)(totalFinalAtk * 0.10), (long)(totalFinalAtk - totalFinalDef));
                long defPowerBefore = target.DefPowerCurrent;
                long defPowerAfter = Math.Max(0, defPowerBefore - netDamage);

                target.DefPowerCurrent = (int)defPowerAfter;

                int points = (int)(netDamage / 100);
                if (points <= 0) points = 5;

                result.Success = true;
                result.DamageDealt = netDamage;
                result.TargetDefPowerBefore = defPowerBefore;
                result.TargetDefPowerAfter = defPowerAfter;
                result.OpponentName = target.Nickname;
                result.PointsEarned = points;

                result.CombatLogs.Add($"[DECK CLASH ENGAGED] Attacking Deck struck opposing leader {target.Nickname} ({target.Role}).");
                result.CombatLogs.Add($"[BARRIER CHECKS] Opposing S.H.I.E.L.D. defensive array absorbed {totalFinalDef:N0} points.");
                result.CombatLogs.Add($"[TACTICAL ENGAGEMENT] Dealt {netDamage:N0} net damage to target shield reserves.");

                if (defPowerAfter == 0)
                {
                    result.TargetFullyDefeated = true;
                    int defeatBonus = 500;
                    points += defeatBonus;
                    result.PointsEarned += defeatBonus;

                    result.CombatLogs.Add($"[DEFENSIVE VICTORY] Target Commander {target.Nickname} has been neutralized! Earned +{defeatBonus} Valor bonus.");
                }
                else
                {
                    result.CombatLogs.Add($"[TARGET STATUS] Remaining defensive power: {defPowerAfter:N0} / {target.DefPowerMax:N0}.");
                }

                // Serialize defensive leaders list back
                var updatedLeadersJson = JsonSerializer.Serialize(opposingLeaders);
                if (isAllianceA)
                {
                    battle.AllianceBDefensiveLeadersJson = updatedLeadersJson;
                }
                else
                {
                    battle.AllianceADefensiveLeadersJson = updatedLeadersJson;
                }

                RecordEventPoints(profileId, eventId, points, isAllianceA, battle);
            }

            _dbContext.SaveChanges();
            return result;
        }

        private void RecordEventPoints(int profileId, string eventId, int points, bool isAllianceA, AllianceWarBattle battle)
        {
            if (isAllianceA)
            {
                battle.AllianceAValorCurrent += points;
            }
            else
            {
                battle.AllianceBValorCurrent += points;
            }

            _eventEngine.RecordEventPoints(profileId, eventId, points);
        }

        private void ConcludeBattle(AllianceWarBattle battle)
        {
            battle.Status = "Concluded";
            if (battle.AllianceAHealthCurrent > battle.AllianceBHealthCurrent)
            {
                battle.WinnerAllianceId = battle.AllianceAId;
            }
            else if (battle.AllianceBHealthCurrent > battle.AllianceAHealthCurrent)
            {
                battle.WinnerAllianceId = battle.AllianceBId;
            }
            else
            {
                // Tiebreaker: Whoever earned more Valor wins
                battle.WinnerAllianceId = battle.AllianceAValorCurrent >= battle.AllianceBValorCurrent 
                    ? battle.AllianceAId 
                    : battle.AllianceBId;
            }
            _dbContext.SaveChanges();
        }
    }
}
