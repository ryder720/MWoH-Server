using System;
using System.Collections.Generic;
using MwohServer.Models;

namespace MwohServer.Services
{
    public class PlayerCardCombatStats
    {
        public PlayerCard Card { get; set; } = null!;
        public int BaseAtk { get; set; }
        public int BaseDef { get; set; }
        public int ActiveBuffPercentageAtk { get; set; }
        public int ActiveBuffPercentageDef { get; set; }
        public int ActiveDebuffPercentageAtk { get; set; }
        public int ActiveDebuffPercentageDef { get; set; }
        
        public int FinalAtk => Math.Max(1, (int)Math.Round(BaseAtk * (1.0 + (ActiveBuffPercentageAtk - ActiveDebuffPercentageAtk) / 100.0)));
        public int FinalDef => Math.Max(1, (int)Math.Round(BaseDef * (1.0 + (ActiveBuffPercentageDef - ActiveDebuffPercentageDef) / 100.0)));
    }

    public interface ICardAbilityEvaluator
    {
        CardAbility? ParseEffect(string abilityName, string effectText, int abilityLevel = 1);
        int GetBaseEffectiveness(CardAbility ability);
        int GetCurrentEffectiveness(CardAbility ability);
        List<PlayerCardCombatStats> EvaluateDeck(List<PlayerCard> friendlyDeck, List<PlayerCard> opposingDeck, bool isAttackingDeck);
    }
}
