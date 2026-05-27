using System;

namespace MwohServer.Models
{
    public enum AbilityIntensity
    {
        Partially,
        Notably,
        Remarkably,
        Significantly,
        Extremely,
        Extraordinarily
    }

    public enum AbilityScope
    {
        Self,
        AlignmentSpeed,
        AlignmentBruiser,
        AlignmentTactics,
        AlignmentsDual, // e.g. "Bruisers/Tactics"
        FactionSuperHero,
        FactionVillain,
        TeamAllies,
        TeamEnemies
    }

    public enum AbilityStat
    {
        Atk,
        Def,
        AtkDef
    }

    public enum AbilityAction
    {
        Strengthen,
        Weaken
    }

    public class CardAbility
    {
        public string AbilityName { get; set; } = string.Empty;
        public string RawEffect { get; set; } = string.Empty;
        public AbilityIntensity Intensity { get; set; }
        public AbilityScope Scope { get; set; }
        public AbilityStat AffectedStat { get; set; }
        public AbilityAction Action { get; set; }
        public int AbilityLevel { get; set; } = 1;
    }
}
