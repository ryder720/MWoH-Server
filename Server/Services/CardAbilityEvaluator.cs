using System;
using System.Collections.Generic;
using System.Linq;
using MwohServer.Models;

namespace MwohServer.Services
{
    public class CardAbilityEvaluator : ICardAbilityEvaluator
    {
        public CardAbility? ParseEffect(string abilityName, string effectText, int abilityLevel = 1)
        {
            if (string.IsNullOrWhiteSpace(effectText)) return null;

            string text = effectText.Trim().ToLowerInvariant();

            // 1. Determine Action (Buff vs Debuff)
            AbilityAction action = AbilityAction.Strengthen;
            if (text.Contains("lower") || text.Contains("weaken") || text.Contains("degrade") || text.Contains("reduce"))
            {
                action = AbilityAction.Weaken;
            }

            // 2. Determine Intensity
            AbilityIntensity intensity = AbilityIntensity.Notably; // Default standard Rare
            if (text.Contains("partially") || text.Contains("slightly"))
            {
                intensity = AbilityIntensity.Partially;
            }
            else if (text.Contains("remarkably") || text.Contains("greatly"))
            {
                intensity = AbilityIntensity.Remarkably;
            }
            else if (text.Contains("significantly"))
            {
                intensity = AbilityIntensity.Significantly;
            }
            else if (text.Contains("extremely"))
            {
                intensity = AbilityIntensity.Extremely;
            }
            else if (text.Contains("extraordinarily") || text.Contains("massively"))
            {
                intensity = AbilityIntensity.Extraordinarily;
            }
            else if (text.Contains("notably") || text.Contains("strengthen") || text.Contains("raise") || text.Contains("harden") || text.Contains("weaken") || text.Contains("lower"))
            {
                intensity = AbilityIntensity.Notably;
            }

            // 3. Determine Affected Stat
            AbilityStat stat = AbilityStat.Atk;
            if (text.Contains("atk/def") || text.Contains("atk & def") || (text.Contains("atk") && text.Contains("def")))
            {
                stat = AbilityStat.AtkDef;
            }
            else if (text.Contains("atk"))
            {
                stat = AbilityStat.Atk;
            }
            else if (text.Contains("def") || text.Contains("harden"))
            {
                stat = AbilityStat.Def;
            }

            // 4. Determine Target Scope
            AbilityScope scope = AbilityScope.TeamAllies; // Fallback to all team

            if (text.Contains("self"))
            {
                scope = AbilityScope.Self;
            }
            else if (text.Contains("opposing") || text.Contains("opponents"))
            {
                scope = AbilityScope.TeamEnemies;
            }
            else if (text.Contains("bruisers/tactics") || text.Contains("speeds/bruisers") || text.Contains("speeds/tactics") || 
                     text.Contains("bruiser/tactic") || text.Contains("speed/bruiser") || text.Contains("speed/tactic"))
            {
                scope = AbilityScope.AlignmentsDual;
            }
            else if (text.Contains("speeds") || text.Contains("speed"))
            {
                scope = AbilityScope.AlignmentSpeed;
            }
            else if (text.Contains("bruisers") || text.Contains("bruiser"))
            {
                scope = AbilityScope.AlignmentBruiser;
            }
            else if (text.Contains("tactics") || text.Contains("tactic"))
            {
                scope = AbilityScope.AlignmentTactics;
            }
            else if (text.Contains("heroes") || text.Contains("hero"))
            {
                scope = AbilityScope.FactionSuperHero;
            }
            else if (text.Contains("villains") || text.Contains("villain"))
            {
                scope = AbilityScope.FactionVillain;
            }
            else if (text.Contains("team") || text.Contains("allies") || text.Contains("your deck"))
            {
                scope = AbilityScope.TeamAllies;
            }

            return new CardAbility
            {
                AbilityName = abilityName,
                RawEffect = effectText,
                Intensity = intensity,
                Scope = scope,
                AffectedStat = stat,
                Action = action,
                AbilityLevel = Math.Max(1, abilityLevel)
            };
        }

        public int GetBaseEffectiveness(CardAbility ability)
        {
            var scope = ability.Scope;
            var stat = ability.AffectedStat;
            var intensity = ability.Intensity;

            return scope switch
            {
                AbilityScope.Self => stat switch
                {
                    AbilityStat.AtkDef => intensity switch
                    {
                        AbilityIntensity.Partially => 9,
                        AbilityIntensity.Notably => 12,
                        AbilityIntensity.Remarkably => 24,
                        AbilityIntensity.Significantly => 36,
                        AbilityIntensity.Extremely => 48,
                        AbilityIntensity.Extraordinarily => 60,
                        _ => 12
                    },
                    _ => intensity switch // Single stat (ATK or DEF)
                    {
                        AbilityIntensity.Partially => 12,
                        AbilityIntensity.Notably => 24,
                        AbilityIntensity.Remarkably => 36,
                        AbilityIntensity.Significantly => 48,
                        AbilityIntensity.Extremely => 60,
                        AbilityIntensity.Extraordinarily => 72,
                        _ => 24
                    }
                },
                
                AbilityScope.FactionSuperHero or AbilityScope.FactionVillain => stat switch
                {
                    AbilityStat.AtkDef => intensity switch
                    {
                        AbilityIntensity.Partially => 3,
                        AbilityIntensity.Notably => 7,
                        AbilityIntensity.Remarkably => 12,
                        AbilityIntensity.Significantly => 15,
                        AbilityIntensity.Extremely => 18,
                        AbilityIntensity.Extraordinarily => 24,
                        _ => 12
                    },
                    _ => intensity switch // Single stat
                    {
                        AbilityIntensity.Partially => 5,
                        AbilityIntensity.Notably => 10,
                        AbilityIntensity.Remarkably => 15,
                        AbilityIntensity.Significantly => 19,
                        AbilityIntensity.Extremely => 23,
                        AbilityIntensity.Extraordinarily => 30,
                        _ => 19
                    }
                },

                AbilityScope.AlignmentSpeed or AbilityScope.AlignmentBruiser or AbilityScope.AlignmentTactics => stat switch
                {
                    AbilityStat.AtkDef => intensity switch
                    {
                        AbilityIntensity.Partially => 3,
                        AbilityIntensity.Notably => 6,
                        AbilityIntensity.Remarkably => 9,
                        AbilityIntensity.Significantly => 12,
                        AbilityIntensity.Extremely => 16,
                        AbilityIntensity.Extraordinarily => 19,
                        _ => 9
                    },
                    _ => intensity switch // Single stat
                    {
                        AbilityIntensity.Partially => 4,
                        AbilityIntensity.Notably => 8,
                        AbilityIntensity.Remarkably => 12,
                        AbilityIntensity.Significantly => 16,
                        AbilityIntensity.Extremely => 20,
                        AbilityIntensity.Extraordinarily => 23,
                        _ => 12
                    }
                },

                AbilityScope.AlignmentsDual => stat switch
                {
                    AbilityStat.AtkDef => intensity switch
                    {
                        AbilityIntensity.Partially => 2,
                        AbilityIntensity.Notably => 5,
                        AbilityIntensity.Remarkably => 8,
                        AbilityIntensity.Significantly => 11,
                        AbilityIntensity.Extremely => 14,
                        AbilityIntensity.Extraordinarily => 18,
                        _ => 8
                    },
                    _ => intensity switch // Single stat
                    {
                        AbilityIntensity.Partially => 4,
                        AbilityIntensity.Notably => 8,
                        AbilityIntensity.Remarkably => 12,
                        AbilityIntensity.Significantly => 16,
                        AbilityIntensity.Extremely => 20,
                        AbilityIntensity.Extraordinarily => 23,
                        _ => 12
                    }
                },

                _ => stat switch // TeamAllies / TeamEnemies
                {
                    AbilityStat.AtkDef => intensity switch
                    {
                        AbilityIntensity.Partially => 2,
                        AbilityIntensity.Notably => 4,
                        AbilityIntensity.Remarkably => 7,
                        AbilityIntensity.Significantly => 9,
                        AbilityIntensity.Extremely => 12,
                        AbilityIntensity.Extraordinarily => 15,
                        _ => 7
                    },
                    _ => intensity switch // Single stat
                    {
                        AbilityIntensity.Partially => 3,
                        AbilityIntensity.Notably => 6,
                        AbilityIntensity.Remarkably => 9,
                        AbilityIntensity.Significantly => 12,
                        AbilityIntensity.Extremely => 16,
                        AbilityIntensity.Extraordinarily => 20,
                        _ => 9
                    }
                }
            };
        }

        public int GetCurrentEffectiveness(CardAbility ability)
        {
            int baseVal = GetBaseEffectiveness(ability);
            int level = ability.AbilityLevel;

            if (level <= 1) return baseVal;

            int bonus = (level - 1) * 1;
            if (level >= 10)
            {
                // Level 10 gives double the standard level step increase (+2% final jump instead of +1%)
                bonus += 1;
            }

            return baseVal + bonus;
        }

        public List<PlayerCardCombatStats> EvaluateDeck(List<PlayerCard> friendlyDeck, List<PlayerCard> opposingDeck, bool isAttackingDeck)
        {
            var friendlyStats = friendlyDeck.Select(c => new PlayerCardCombatStats
            {
                Card = c,
                BaseAtk = c.CurrentAtk,
                BaseDef = c.CurrentDef
            }).ToList();

            var opposingStats = opposingDeck.Select(c => new PlayerCardCombatStats
            {
                Card = c,
                BaseAtk = c.CurrentAtk,
                BaseDef = c.CurrentDef
            }).ToList();

            // 1. Process Friendly Deck Abilities
            foreach (var fStats in friendlyStats)
            {
                var card = fStats.Card;
                if (card.CardTemplate == null) continue;

                var ability = ParseEffect(card.CardTemplate.AbilityName, card.CardTemplate.AbilityEffect, card.AbilityLevel);
                if (ability == null || string.IsNullOrWhiteSpace(ability.AbilityName)) continue;

                int value = GetCurrentEffectiveness(ability);

                if (ability.Action == AbilityAction.Strengthen)
                {
                    // Buff friendly deck cards in scope
                    foreach (var target in friendlyStats)
                    {
                        if (IsCardInScope(target.Card, ability.Scope, card))
                        {
                            if (ability.AffectedStat == AbilityStat.Atk || ability.AffectedStat == AbilityStat.AtkDef)
                            {
                                target.ActiveBuffPercentageAtk += value;
                            }
                            if (ability.AffectedStat == AbilityStat.Def || ability.AffectedStat == AbilityStat.AtkDef)
                            {
                                target.ActiveBuffPercentageDef += value;
                            }
                        }
                    }
                }
                else if (ability.Action == AbilityAction.Weaken)
                {
                    // Debuff opposing deck cards in scope
                    foreach (var target in opposingStats)
                    {
                        if (IsCardInScope(target.Card, ability.Scope, card))
                        {
                            if (ability.AffectedStat == AbilityStat.Atk || ability.AffectedStat == AbilityStat.AtkDef)
                            {
                                target.ActiveDebuffPercentageAtk += value;
                            }
                            if (ability.AffectedStat == AbilityStat.Def || ability.AffectedStat == AbilityStat.AtkDef)
                            {
                                target.ActiveDebuffPercentageDef += value;
                            }
                        }
                    }
                }
            }

            // 2. Process Opposing Deck Abilities (whose targets affect us or themselves)
            foreach (var oStats in opposingStats)
            {
                var card = oStats.Card;
                if (card.CardTemplate == null) continue;

                var ability = ParseEffect(card.CardTemplate.AbilityName, card.CardTemplate.AbilityEffect, card.AbilityLevel);
                if (ability == null || string.IsNullOrWhiteSpace(ability.AbilityName)) continue;

                int value = GetCurrentEffectiveness(ability);

                if (ability.Action == AbilityAction.Weaken)
                {
                    // Opponent weakening abilities debuff friendly deck cards in scope
                    foreach (var target in friendlyStats)
                    {
                        if (IsCardInScope(target.Card, ability.Scope, card))
                        {
                            if (ability.AffectedStat == AbilityStat.Atk || ability.AffectedStat == AbilityStat.AtkDef)
                            {
                                target.ActiveDebuffPercentageAtk += value;
                            }
                            if (ability.AffectedStat == AbilityStat.Def || ability.AffectedStat == AbilityStat.AtkDef)
                            {
                                target.ActiveDebuffPercentageDef += value;
                            }
                        }
                    }
                }
            }

            return friendlyStats;
        }

        private bool IsCardInScope(PlayerCard card, AbilityScope scope, PlayerCard abilityOwner)
        {
            switch (scope)
            {
                case AbilityScope.Self:
                    return card.Id == abilityOwner.Id;
                
                case AbilityScope.AlignmentSpeed:
                    return string.Equals(card.CardTemplate?.Alignment, "Speed", StringComparison.OrdinalIgnoreCase);

                case AbilityScope.AlignmentBruiser:
                    return string.Equals(card.CardTemplate?.Alignment, "Bruiser", StringComparison.OrdinalIgnoreCase);

                case AbilityScope.AlignmentTactics:
                    return string.Equals(card.CardTemplate?.Alignment, "Tactics", StringComparison.OrdinalIgnoreCase);

                case AbilityScope.AlignmentsDual:
                    var alignment = card.CardTemplate?.Alignment ?? "";
                    var ownerAlignment = abilityOwner.CardTemplate?.Alignment ?? "";
                    
                    var raw = abilityOwner.CardTemplate?.AbilityEffect?.ToLowerInvariant() ?? "";
                    bool matchesBruiser = raw.Contains("bruiser");
                    bool matchesTactics = raw.Contains("tactic");
                    bool matchesSpeed = raw.Contains("speed");

                    if (matchesBruiser && string.Equals(alignment, "Bruiser", StringComparison.OrdinalIgnoreCase)) return true;
                    if (matchesTactics && string.Equals(alignment, "Tactics", StringComparison.OrdinalIgnoreCase)) return true;
                    if (matchesSpeed && string.Equals(alignment, "Speed", StringComparison.OrdinalIgnoreCase)) return true;

                    // Fallback to matching owner alignment
                    return string.Equals(alignment, ownerAlignment, StringComparison.OrdinalIgnoreCase);

                case AbilityScope.FactionSuperHero:
                    return string.Equals(card.CardTemplate?.Faction, "Super Hero", StringComparison.OrdinalIgnoreCase);

                case AbilityScope.FactionVillain:
                    return string.Equals(card.CardTemplate?.Faction, "Villain", StringComparison.OrdinalIgnoreCase);

                case AbilityScope.TeamAllies:
                case AbilityScope.TeamEnemies:
                    return true; // Targets everything in the target deck

                default:
                    return false;
            }
        }
    }
}
