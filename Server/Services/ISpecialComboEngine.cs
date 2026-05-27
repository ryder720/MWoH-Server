using System.Collections.Generic;
using MwohServer.Models;

namespace MwohServer.Services
{
    public class SpecialComboResult
    {
        public string ComboId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Triggered { get; set; }
        public string LogLine { get; set; } = string.Empty;
        
        public string EffectText { get; set; } = string.Empty;
        
        // Boost details
        public ComboTarget Target { get; set; }
        public ComboStat AffectedStat { get; set; }
        public ComboScope Scope { get; set; }
        public string[] ScopeDetail { get; set; } = System.Array.Empty<string>();
        public int PowerValue { get; set; } // Buff/debuff percentage e.g., 5 for 5%
    }

    public enum ComboTarget { Friendly, Opposing }
    public enum ComboStat { Atk, Def, AtkDef }
    public enum ComboScope { All, Alignment, Faction, SpecificCharacters }

    public interface ISpecialComboEngine
    {
        List<SpecialComboResult> ProcessDeckCombos(List<PlayerCard> deck, bool isAttacking);
    }
}
