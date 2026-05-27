using System;
using System.Collections.Generic;
using System.Linq;
using MwohServer.Models;

namespace MwohServer.Services
{
    public class SpecialComboDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string[] RequiredCharacters { get; set; } = Array.Empty<string>();
        public string EffectText { get; set; } = string.Empty;
        public double TriggerChance { get; set; } // e.g., 0.80
        public int PowerValue { get; set; } // Buff percentage e.g., 5
        public bool IsDefenseCombo { get; set; }

        public ComboTarget Target { get; set; }
        public ComboStat AffectedStat { get; set; }
        public ComboScope Scope { get; set; }
        public string[] ScopeDetail { get; set; } = Array.Empty<string>();

        // Wildcard Checks
        public bool Requires5Females { get; set; }
        public bool Requires5Villains { get; set; }
        public bool Requires5Heroes { get; set; }
        public bool Requires5MaleHeroes { get; set; }
        public bool Requires5MaleVillains { get; set; }
        public bool Requires5SameName { get; set; }
        public string SameNameTarget { get; set; } = string.Empty;
    }

    public class SpecialComboEngine : ISpecialComboEngine
    {
        private static readonly List<SpecialComboDefinition> Registry = new List<SpecialComboDefinition>();

        static SpecialComboEngine()
        {
            InitializeRegistry();
        }

        public List<SpecialComboResult> ProcessDeckCombos(List<PlayerCard> deck, bool isAttacking)
        {
            var results = new List<SpecialComboResult>();
            if (deck == null || deck.Count == 0) return results;

            var random = new Random();

            // Extract base character names from deck
            var characterMapping = deck
                .Where(c => c.CardTemplate != null)
                .Select(c => new { Card = c, Character = GetCharacterName(c.CardTemplate!.Title) })
                .ToList();

            var uniqueCharacters = characterMapping.Select(cm => cm.Character).Distinct().ToList();

            // Wildcard parameters
            int totalFemales = deck.Count(c => string.Equals(c.CardTemplate?.Gender, "Female", StringComparison.OrdinalIgnoreCase));
            int totalVillains = deck.Count(c => string.Equals(c.CardTemplate?.Faction, "Villain", StringComparison.OrdinalIgnoreCase));
            int totalHeroes = deck.Count(c => string.Equals(c.CardTemplate?.Faction, "Super Hero", StringComparison.OrdinalIgnoreCase));
            int totalMaleHeroes = deck.Count(c => string.Equals(c.CardTemplate?.Gender, "Male", StringComparison.OrdinalIgnoreCase) && string.Equals(c.CardTemplate?.Faction, "Super Hero", StringComparison.OrdinalIgnoreCase));
            int totalMaleVillains = deck.Count(c => string.Equals(c.CardTemplate?.Gender, "Male", StringComparison.OrdinalIgnoreCase) && string.Equals(c.CardTemplate?.Faction, "Villain", StringComparison.OrdinalIgnoreCase));

            foreach (var combo in Registry)
            {
                // Verify defense or attack context matches
                if (combo.IsDefenseCombo == isAttacking) continue; // Defense combos trigger on defense (isAttacking = false), attack on attack

                bool isMatch = false;

                if (combo.Requires5Females)
                {
                    isMatch = totalFemales >= 5;
                }
                else if (combo.Requires5Villains)
                {
                    isMatch = totalVillains >= 5;
                }
                else if (combo.Requires5Heroes)
                {
                    isMatch = totalHeroes >= 5;
                }
                else if (combo.Requires5MaleHeroes)
                {
                    isMatch = totalMaleHeroes >= 5;
                }
                else if (combo.Requires5MaleVillains)
                {
                    isMatch = totalMaleVillains >= 5;
                }
                else if (combo.Requires5SameName)
                {
                    var count = characterMapping.Count(cm => string.Equals(cm.Character, combo.SameNameTarget, StringComparison.OrdinalIgnoreCase));
                    isMatch = count >= 5;
                }
                else if (combo.RequiredCharacters.Length > 0)
                {
                    // Verify ALL required characters are present in distinct cards of the deck
                    isMatch = combo.RequiredCharacters.All(reqChar => 
                        characterMapping.Any(cm => string.Equals(cm.Character, reqChar, StringComparison.OrdinalIgnoreCase))
                    );
                }

                if (isMatch)
                {
                    bool triggers = random.NextDouble() < combo.TriggerChance;
                    string actionWord = combo.AffectedStat == ComboStat.Def ? "harden" : "strengthen";
                    if (combo.Target == ComboTarget.Opposing) actionWord = "weaken";

                    var res = new SpecialComboResult
                    {
                        ComboId = combo.Id,
                        Name = combo.Name,
                        Triggered = triggers,
                        EffectText = combo.EffectText,
                        Target = combo.Target,
                        AffectedStat = combo.AffectedStat,
                        Scope = combo.Scope,
                        ScopeDetail = combo.ScopeDetail,
                        PowerValue = combo.PowerValue,
                        LogLine = triggers 
                            ? $"[SPECIAL COMBO] {combo.Name} activated! Effect: {combo.EffectText} by +{combo.PowerValue}% (Usage roll passed)."
                            : $"[SPECIAL COMBO] {combo.Name} detected in squad setup, but usage roll failed."
                    };
                    results.Add(res);
                }
            }

            return results;
        }

        public static string GetCharacterName(string title)
        {
            if (string.IsNullOrEmpty(title)) return string.Empty;
            var t = title.ToLowerInvariant();
            
            if (t.Contains("iron spider")) return "Iron Spider-Man";
            if (t.Contains("spider-man") || t.Contains("spider man") || t.Contains("peter parker")) return "Spider-Man";
            if (t.Contains("iron man") || t.Contains("tony stark") || t.Contains("hulkbuster")) return "Iron Man";
            if (t.Contains("captain america") || t.Contains("steve rogers")) return "Captain America";
            if (t.Contains("wolverine") || t.Contains("logan")) return "Wolverine";
            if (t.Contains("rocket raccoon") || t.Contains("rocket raccoon")) return "Rocket Raccoon";
            if (t.Contains("star-lord") || t.Contains("star lord") || t.Contains("peter quill")) return "Star-Lord";
            if (t.Contains("black widow") || t.Contains("natasha")) return "Black Widow";
            if (t.Contains("deadpool") || t.Contains("wade wilson")) return "Deadpool";
            if (t.Contains("ghost rider") || t.Contains("johnny blaze")) return "Ghost Rider";
            if (t.Contains("punisher") || t.Contains("frank castle")) return "Punisher";
            if (t.Contains("daredevil") || t.Contains("matt murdock")) return "Daredevil";
            if (t.Contains("sentinel")) return "Sentinel";
            if (t.Contains("thanos")) return "Thanos";
            if (t.Contains("apocalypse")) return "Apocalypse";
            if (t.Contains("cyclops") || t.Contains("scott summers")) return "Cyclops";
            if (t.Contains("jean grey")) return "Jean Grey";
            if (t.Contains("goblin queen") || t.Contains("madelyne pryor")) return "Goblin Queen";
            if (t.Contains("magneto") || t.Contains("erik lehnsherr")) return "Magneto";
            if (t.Contains("mystique") || t.Contains("raven darkholme")) return "Mystique";
            if (t.Contains("juggernaut") || t.Contains("cain marko")) return "Juggernaut";
            if (t.Contains("thor")) return "Thor";
            if (t.Contains("loki")) return "Loki";
            if (t.Contains("maria hill")) return "Maria Hill";
            if (t.Contains("doctor strange") || t.Contains("dr. strange") || t.Contains("stephen strange")) return "Doctor Strange";
            if (t.Contains("doctor doom") || t.Contains("dr. doom") || t.Contains("victor von doom")) return "Doctor Doom";
            if (t.Contains("scarlet witch") || t.Contains("wanda")) return "Scarlet Witch";
            if (t.Contains("daken")) return "Daken";
            if (t.Contains("sabretooth") || t.Contains("victor creed")) return "Sabretooth";
            if (t.Contains("sin")) return "Sin";
            if (t.Contains("red skull") || t.Contains("johann schmidt")) return "Red Skull";
            if (t.Contains("serpent")) return "Serpent";
            if (t.Contains("death")) return "Death";
            if (t.Contains("skaar")) return "Skaar";
            if (t.Contains("a-bomb") || t.Contains("rick jones")) return "A-Bomb";
            if (t.Contains("hulk") && !t.Contains("hulkbuster") && !t.Contains("she-hulk")) return "Hulk";
            if (t.Contains("she-hulk")) return "She-Hulk";
            if (t.Contains("mr. fantastic") || t.Contains("reed richards")) return "Mr. Fantastic";
            if (t.Contains("invisible woman") || t.Contains("sue storm")) return "Invisible Woman";
            if (t.Contains("human torch") || t.Contains("johnny storm")) return "Human Torch";
            if (t.Contains("the thing") || t.Contains("ben grimm")) return "The Thing";
            if (t.Contains("franklin richards")) return "Franklin";
            if (t.Contains("hope summers")) return "Hope Summers";
            if (t.Contains("cable") || t.Contains("nathan summers")) return "Cable";
            if (t.Contains("beast") || t.Contains("hank mccoy")) return "Beast";
            if (t.Contains("nightcrawler") || t.Contains("kurt wagner")) return "Nightcrawler";
            if (t.Contains("supergiant")) return "Supergiant";
            if (t.Contains("ebony maw")) return "Ebony Maw";
            if (t.Contains("iron patriot") || t.Contains("norman osborn")) return "Iron Patriot";
            if (t.Contains("psylocke") || t.Contains("betsy braddock")) return "Psylocke";
            if (t.Contains("ultron")) return "Ultron";
            if (t.Contains("iron monger")) return "Iron Monger";
            if (t.Contains("vision")) return "Vision";
            if (t.Contains("x-23") || t.Contains("laura kinney")) return "X-23";
            if (t.Contains("emma frost")) return "Emma Frost";
            if (t.Contains("nova") || t.Contains("richard rider") || t.Contains("sam alexander")) return "Nova";
            if (t.Contains("odin")) return "Odin";
            if (t.Contains("galactus")) return "Galactus";
            if (t.Contains("silver surfer") || t.Contains("norrin radd")) return "Silver Surfer";
            if (t.Contains("omega red")) return "Omega Red";
            if (t.Contains("luke cage")) return "Luke Cage";
            if (t.Contains("drax")) return "Drax";
            if (t.Contains("miek")) return "Miek";
            if (t.Contains("groot")) return "Groot";
            if (t.Contains("spider-girl")) return "Spider-Girl";
            if (t.Contains("spider-woman")) return "Spider-Woman";
            if (t.Contains("angel") || t.Contains("warren worthington")) return "Angel";
            if (t.Contains("wasp") || t.Contains("janet van dyne")) return "Wasp";
            if (t.Contains("lockheed")) return "Lockheed";
            if (t.Contains("beta-ray bill") || t.Contains("beta ray bill")) return "Beta-Ray Bill";
            if (t.Contains("ragnarok")) return "Ragnarok";
            if (t.Contains("zabu")) return "Zabu";
            if (t.Contains("throg")) return "Throg";
            if (t.Contains("lockjaw")) return "Lockjaw";
            if (t.Contains("professor x") || t.Contains("charles xavier")) return "Professor X";
            if (t.Contains("fantomex")) return "Fantomex";
            if (t.Contains("mandarin")) return "Mandarin";
            if (t.Contains("storm") || t.Contains("ororo munroe")) return "Storm";
            if (t.Contains("iron fist") || t.Contains("danny rand")) return "Iron Fist";
            if (t.Contains("valkyrie") || t.Contains("brunnhilde")) return "Valkyrie";
            if (t.Contains("sif")) return "Sif";
            if (t.Contains("black bolt")) return "Black Bolt";
            if (t.Contains("black panther") || t.Contains("t'challa")) return "Black Panther";
            if (t.Contains("kingpin") || t.Contains("wilson fisk")) return "Kingpin";
            if (t.Contains("rhino") || t.Contains("aleksei sytsevich")) return "Rhino";
            if (t.Contains("wonder man") || t.Contains("simon williams")) return "Wonder Man";
            if (t.Contains("colossus") || t.Contains("piotr rasputin")) return "Colossus";
            if (t.Contains("kitty pryde")) return "Kitty Pryde";
            if (t.Contains("sandman") || t.Contains("flint marko")) return "Sandman";
            if (t.Contains("builder")) return "Builder";
            if (t.Contains("hulkling")) return "Hulkling";
            if (t.Contains("corvus glaive")) return "Corvus Glaive";
            if (t.Contains("proxima midnight")) return "Proxima Midnight";
            if (t.Contains("thane")) return "Thane";
            if (t.Contains("gamora")) return "Gamora";
            if (t.Contains("jubilee")) return "Jubilee";
            if (t.Contains("elektra")) return "Elektra";
            if (t.Contains("veranke")) return "Veranke";
            if (t.Contains("super-skrull") || t.Contains("super skrull") || t.Contains("skrull")) return "Super-Skrull";
            if (t.Contains("bullseye")) return "Bullseye";
            if (t.Contains("hawkeye") || t.Contains("clint barton")) return "Hawkeye";
            if (t.Contains("mister sinister") || t.Contains("mr. sinister") || t.Contains("nathaniel essex")) return "Mister Sinister";
            if (t.Contains("the hood") || t.Contains("parker robbins")) return "The Hood";
            if (t.Contains("sentry") || t.Contains("robert reynolds") || t.Contains("void")) return "Sentry";
            if (t.Contains("quicksilver") || t.Contains("pietro maximoff")) return "Quicksilver";
            if (t.Contains("doctor spectrum") || t.Contains("dr. spectrum")) return "Doctor Spectrum";
            if (t.Contains("modok") || t.Contains("george tarleton")) return "MODOK";
            if (t.Contains("hercules")) return "Hercules";
            if (t.Contains("enchantress") || t.Contains("amora")) return "Enchantress";
            if (t.Contains("medusa")) return "Medusa";
            if (t.Contains("giant man") || t.Contains("giant-man") || t.Contains("hank pym") || t.Contains("ant-man") || t.Contains("ant man")) return "Giant Man";
            if (t.Contains("morbius") || t.Contains("michael morbius")) return "Morbius";
            if (t.Contains("doctor octopus") || t.Contains("doc ock") || t.Contains("otto octavius")) return "Doctor Octopus";
            if (t.Contains("scarlet spider") || t.Contains("ben reilly") || t.Contains("kaine parker")) return "Scarlet Spider";
            if (t.Contains("taskmaster") || t.Contains("tony masters")) return "Taskmaster";
            if (t.Contains("war machine") || t.Contains("james rhodes") || t.Contains("rhodey")) return "War Machine";
            if (t.Contains("harrier")) return "Harrier";

            return title;
        }

        private static void InitializeRegistry()
        {
            // Helper to build combos
            void AddCombo(string id, string name, string[] chars, string effect, double chance, int power, bool isDef, ComboTarget tgt, ComboStat stat, ComboScope scope, string[] scopeDet)
            {
                Registry.Add(new SpecialComboDefinition
                {
                    Id = id,
                    Name = name,
                    RequiredCharacters = chars,
                    EffectText = effect,
                    TriggerChance = chance,
                    PowerValue = power,
                    IsDefenseCombo = isDef,
                    Target = tgt,
                    AffectedStat = stat,
                    Scope = scope,
                    ScopeDetail = scopeDet
                });
            }

            void AddWildcardCombo(string id, string name, string effect, double chance, int power, bool isDef, ComboTarget tgt, ComboStat stat, ComboScope scope, string[] scopeDet, bool fem, bool vil, bool her, bool mHer, bool mVil, bool same, string sameName)
            {
                Registry.Add(new SpecialComboDefinition
                {
                    Id = id,
                    Name = name,
                    RequiredCharacters = Array.Empty<string>(),
                    EffectText = effect,
                    TriggerChance = chance,
                    PowerValue = power,
                    IsDefenseCombo = isDef,
                    Target = tgt,
                    AffectedStat = stat,
                    Scope = scope,
                    ScopeDetail = scopeDet,
                    Requires5Females = fem,
                    Requires5Villains = vil,
                    Requires5Heroes = her,
                    Requires5MaleHeroes = mHer,
                    Requires5MaleVillains = mVil,
                    Requires5SameName = same,
                    SameNameTarget = sameName
                });
            }

            // --------------------------------------------------
            // 1. 2-Card Attack Combos
            // --------------------------------------------------
            AddCombo("SCHungryForDeath", "Hungry for Death", new[] { "Venom", "Thanos" }, "Strengthen ATK of Villains", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Villain" });
            AddCombo("SCHeyRocky", "Hey Rocky!", new[] { "Spider-Man", "Rocket Raccoon" }, "Strengthen ATK of Speed alignment", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Speed" });
            AddCombo("SCSuperSoldiers", "Super Soldiers", new[] { "Captain America", "Wolverine" }, "Strengthen ATK of Cap & Wolverine", 0.60, 8, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Captain America", "Wolverine" });
            AddCombo("SCMercilessHeroes", "Merciless Heroes", new[] { "Punisher", "Wolverine" }, "Strengthen ATK of Punisher & Wolverine", 0.60, 8, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Punisher", "Wolverine" });
            AddCombo("SCSynergyDriveReactors", "Synergy Drive Reactors", new[] { "Iron Man", "War Machine" }, "Strengthen ATK of Iron Man & War Machine", 0.60, 10, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Iron Man", "War Machine" });
            AddCombo("SCBrotherhood", "Brotherhood", new[] { "Daredevil", "Wolverine" }, "Strengthen ATK of Wolverine & Daredevil", 0.60, 8, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Daredevil", "Wolverine" });
            AddCombo("SCExtraSuit", "Extra Suit", new[] { "Iron Spider-Man", "Iron Man" }, "Strengthen ATK of Iron Spider & Iron Man", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Iron Spider-Man", "Iron Man" });
            AddCombo("SCVeterans", "Veterans", new[] { "Captain America", "Punisher" }, "Strengthen ATK of Tactics alignment", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Tactics" });
            AddCombo("SCUnmasked", "Unmasked", new[] { "Hulkling", "Spider-Man" }, "Strengthen ATK of Speed alignment", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Speed" });
            AddCombo("SCSoRandom", "So Random", new[] { "Deadpool", "Domino" }, "Strengthen ATK of your Team", 0.50, 8, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCObsidianPartners", "Obsidian Partners", new[] { "Corvus Glaive", "Proxima Midnight" }, "Strengthen ATK of Villains", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Villain" });
            AddCombo("SCDeviantGenerations", "Deviant Generations", new[] { "Thanos", "Thane" }, "Strengthen ATK of Thanos & Thane", 0.75, 13, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Thanos", "Thane" });
            AddCombo("SCGuardianGunslingers", "Guardian Gunslingers", new[] { "Rocket Raccoon", "Star-Lord" }, "Strengthen ATK of Speed alignment", 0.90, 4, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Speed" });
            AddCombo("SCLeadGuardians", "Lead Guardians", new[] { "Nova", "Star-Lord" }, "Strengthen ATK of your Heroes", 0.70, 6, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Super Hero" });
            AddCombo("SCLethalLadies", "Lethal Ladies", new[] { "Gamora", "Death" }, "Strengthen ATK of Tactics alignment", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Tactics" });
            AddCombo("SCMutantTeamwork", "Mutant Teamwork", new[] { "Jubilee", "Wolverine" }, "Strengthen ATK of Jubilee & Wolverine", 0.85, 11, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Jubilee", "Wolverine" });
            AddCombo("SCMadTitansWrath", "Mad Titan's Wrath", new[] { "Sentinel", "Thanos" }, "Strengthen ATK of Villains", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Villain" });
            AddCombo("SCBruisingTactics", "Bruising Tactics", new[] { "Sentinel", "Cyclops" }, "Strengthen ATK of Bruisers", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Bruiser" });
            AddCombo("SCVenomousAssault", "Venomous Assault", new[] { "Sentinel", "Venom" }, "Strengthen ATK of Sentinel & Venom", 0.85, 11, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Sentinel", "Venom" });
            AddCombo("SCApocalypticVisions", "Apocalyptic Visions", new[] { "Sentinel", "Apocalypse" }, "Strengthen ATK of Sentinel & Apocalypse", 0.80, 12, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Sentinel", "Apocalypse" });
            AddCombo("SCNinjaInsight", "Ninja Insight", new[] { "Elektra", "Psylocke" }, "Weaken DEF of opposing Team", 0.90, 4, false, ComboTarget.Opposing, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCSkrullDeception", "Skrull Deception", new[] { "Veranke", "Super-Skrull" }, "Weaken DEF of opposing Heroes", 0.80, 6, false, ComboTarget.Opposing, ComboStat.Def, ComboScope.Faction, new[] { "Super Hero" });
            AddCombo("SCFatherAndSon", "Father and Son", new[] { "Cable", "Cyclops" }, "Strengthen ATK of Bruisers", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Bruiser" });
            AddCombo("SCBestBudsForever", "Best Buds Forever", new[] { "Cable", "Deadpool" }, "Strengthen ATK of Cable & Deadpool", 0.80, 12, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Cable", "Deadpool" });
            AddCombo("SCAskaniDestiny", "Askani Destiny", new[] { "Cable", "Apocalypse" }, "Strengthen ATK of Bruisers", 0.70, 6, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Bruiser" });
            AddCombo("SCPsychicSupport", "Psychic Support", new[] { "Cable", "Jean Grey" }, "Strengthen ATK of Heroes", 0.80, 4, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Super Hero" });
            AddCombo("SCMotherandSon", "Mother and Son", new[] { "Cable", "Goblin Queen" }, "Strengthen ATK of Bruisers", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Bruiser" });
            AddCombo("SCHeroicCharge", "Heroic Charge", new[] { "Iron Man", "Spider-Man" }, "Strengthen ATK of Heroes", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Super Hero" });
            AddCombo("SCHeavyOrdnance", "Heavy Ordnance", new[] { "Iron Man", "Punisher" }, "Strengthen ATK of Iron Man & Punisher", 0.70, 15, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Iron Man", "Punisher" });
            AddCombo("SCIroncladStrategy", "Ironclad Strategy", new[] { "Iron Man", "Iron Patriot" }, "Strengthen ATK of Tactics alignment", 0.90, 4, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Tactics" });
            AddCombo("SCTitanicBlast", "Titanic Blast", new[] { "Iron Patriot", "Thanos" }, "Strengthen ATK of Iron Patriot & Thanos", 0.85, 11, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Iron Patriot", "Thanos" });
            AddCombo("SCMercilessTactics", "Merciless Tactics", new[] { "Punisher", "Psylocke" }, "Strengthen ATK of Tactics alignment", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Tactics" });
            AddCombo("SCSavageSkirmish", "Savage Skirmish", new[] { "Daken", "Wolverine" }, "Strengthen ATK of Daken & Wolverine", 0.90, 11, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Daken", "Wolverine" });
            AddCombo("SCBloodthirstyAssault", "Bloodthirsty Assault", new[] { "Daken", "Sabretooth" }, "Strengthen ATK of Villains", 0.85, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Villain" });
            AddCombo("SCPinpointWeakness", "Pinpoint Weakness", new[] { "Bullseye", "Hawkeye" }, "Weaken DEF of opposing Team", 0.90, 4, false, ComboTarget.Opposing, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCAvengingAssembly", "Avenging Assembly", new[] { "Iron Man", "Captain America" }, "Strengthen ATK of Iron Man & Cap", 0.75, 13, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Iron Man", "Captain America" });
            AddCombo("SCBondOfBrothers", "Bond of Brothers", new[] { "Thor", "Loki" }, "Strengthen ATK of Thor & Loki", 0.80, 12, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Thor", "Loki" });
            AddCombo("SCTacticalThunderbolt", "Tactical Thunderbolt", new[] { "Thor", "Maria Hill" }, "Strengthen ATK of Thor & Maria Hill", 0.85, 11, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Thor", "Maria Hill" });
            AddCombo("SCPsychoKillers", "Psycho Killers", new[] { "Green Goblin", "Daken" }, "Strengthen ATK of Villains", 0.90, 4, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Villain" });
            AddCombo("SCCrator&Clone", "Creator & Clone", new[] { "Mister Sinister", "Goblin Queen" }, "Weaken DEF of opposing team", 0.90, 4, false, ComboTarget.Opposing, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCOmegaTelepaths", "Omega Telepaths", new[] { "Jean Grey", "Goblin Queen" }, "Strengthen ATK of Jean & Goblin Queen", 0.90, 11, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Jean Grey", "Goblin Queen" });
            AddCombo("SCDeceivedLovers", "Deceived Lovers", new[] { "Goblin Queen", "Cyclops" }, "Strengthen ATK of Bruisers", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Bruiser" });
            AddCombo("SCDemonHosts", "Demon Hosts", new[] { "Goblin Queen", "The Hood" }, "Weaken DEF of opposing team", 0.80, 5, false, ComboTarget.Opposing, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCBrotherhoodLeaders", "Brotherhood Leaders", new[] { "Magneto", "Mystique" }, "Strengthen ATK of Villains", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Villain" });
            AddCombo("SCUnstoppableForces", "Unstoppable Forces", new[] { "Magneto", "Juggernaut" }, "Strengthen ATK of Magneto & Juggernaut", 0.85, 11, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Magneto", "Juggernaut" });
            AddCombo("SCHeroicTeamUp", "Heroic Team-Up", new[] { "Captain America", "Spider-Man" }, "Strengthen ATK of Heroes", 0.85, 4, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Super Hero" });
            AddCombo("SCEndlessEnmity", "Endless Enmity", new[] { "Captain America", "Red Skull" }, "Strengthen ATK of Cap & Red Skull", 0.90, 11, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Captain America", "Red Skull" });
            AddCombo("SCUnfetteredWrath", "Unfettered Wrath", new[] { "Sentry", "Green Goblin" }, "Weaken DEF of opposing team", 0.70, 6, false, ComboTarget.Opposing, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCFuriousStrength", "Furious Strength", new[] { "Sentry", "Hulk" }, "Strengthen ATK of Sentry & Hulk", 0.75, 14, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Sentry", "Hulk" });
            AddCombo("SCDeathWish", "Death Wish", new[] { "Thanos", "Death" }, "Strengthen ATK of Thanos & Death", 0.85, 11, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Thanos", "Death" });
            AddCombo("SCWorthyLeaders", "Worthy Leaders", new[] { "Sin", "Serpent" }, "Strengthen ATK of Villains", 0.90, 4, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Villain" });
            AddCombo("SCSonAndBrother", "Son and Brother", new[] { "Thor", "Serpent" }, "Strengthen ATK of Thor & Serpent", 0.80, 10, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Thor", "Serpent" });
            AddCombo("SCFatherAndDaughter", "Father and Daughter", new[] { "Sin", "Red Skull" }, "Strengthen ATK of Villains", 0.90, 4, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Villain" });

            // --------------------------------------------------
            // 2. 3-Card Attack Combos
            // --------------------------------------------------
            AddCombo("SCSorcerersThree", "Sorcerers Three", new[] { "Scarlet Witch", "Doctor Strange", "Doctor Doom" }, "DEF decline on opposing team", 0.85, 6, false, ComboTarget.Opposing, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCSpiderBite", "Spider Bite", new[] { "Spider-Man", "Spider-Woman", "Scarlet Spider" }, "DEF decline on opposing team", 0.85, 6, false, ComboTarget.Opposing, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCCovenBanquet", "Coven Banquet", new[] { "Mystique", "Enchantress", "Medusa" }, "DEF decline on opposing team", 0.75, 8, false, ComboTarget.Opposing, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCModernWeapons", "Modern Weapons", new[] { "Sentinel", "Ultron", "Iron Monger" }, "DEF decline on Hero cards of opposing deck", 0.75, 12, false, ComboTarget.Opposing, ComboStat.Def, ComboScope.Faction, new[] { "Super Hero" });
            AddCombo("SCSyntheticStrength", "Synthetic Strength", new[] { "Vision", "Sentinel", "Ultron" }, "Strengthen ATK of Vision, Sentinel, Ultron", 0.85, 6, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Vision", "Sentinel", "Ultron" });
            AddCombo("SCClaws", "Claws", new[] { "Wolverine", "X-23", "Daken" }, "Strengthen ATK of Wolverine, X-23 & Daken", 0.85, 10, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Wolverine", "X-23", "Daken" });
            AddCombo("SCMutantMasterminds", "Mutant Masterminds", new[] { "Magneto", "Emma Frost", "Scarlet Witch" }, "Strengthen ATK of your Team", 0.85, 6, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCMenage", "Menage", new[] { "Cyclops", "Jean Grey", "Emma Frost" }, "Strengthen ATK of your Team", 0.85, 6, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCXPower", "X-Power", new[] { "Wolverine", "Cyclops", "Jean Grey" }, "Strengthen ATK of your Heroes", 0.85, 6, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Super Hero" });
            AddCombo("SCShootingStar", "Shooting Star", new[] { "Human Torch", "Silver Surfer", "Nova" }, "Strengthen ATK of Speed alignment", 0.75, 10, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Speed" });
            AddCombo("SCAvengersBig3", "Avengers Big 3", new[] { "Iron Man", "Captain America", "Thor" }, "Strengthen ATK of Iron Man, Cap & Thor", 0.75, 10, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Iron Man", "Captain America", "Thor" });
            AddCombo("SCUnlimitedPowers", "Unlimited Powers", new[] { "The Thing", "Hulk", "She-Hulk" }, "Strengthen ATK of Bruiser alignment", 0.75, 10, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Bruiser" });
            AddCombo("SCBikersOrganization", "Biker's Organization", new[] { "Ghost Rider", "Human Torch", "Wolverine" }, "Strengthen ATK of GR, Torch & Wolverine", 0.75, 7, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Ghost Rider", "Human Torch", "Wolverine" });
            AddCombo("SCOgreDevilSpider", "Ogre, Devil, Spider", new[] { "Blade", "Ghost Rider", "Spider-Man" }, "Strengthen ATK of Speed alignment", 0.75, 7, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Speed" });
            AddCombo("SCCombatMasters", "Combat Masters", new[] { "Daredevil", "Captain America", "Iron Fist" }, "Strengthen ATK of Tactics alignment", 0.75, 10, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Tactics" });
            AddCombo("SCTheGreatBrains", "The Great Brains", new[] { "Mr. Fantastic", "Iron Man", "Doctor Strange" }, "Strengthen ATK of your Team", 0.75, 7, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCSliceAndDice", "Slice and Dice", new[] { "Sabretooth", "X-23", "Taskmaster" }, "Strengthen ATK of your Villains", 0.75, 10, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Villain" });
            AddCombo("SCDarkBrain", "Dark Brain", new[] { "Morbius", "Doctor Octopus", "Ultron" }, "Strengthen ATK of your team", 0.75, 8, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCBrainTrust", "Brain Trust", new[] { "Mr. Fantastic", "Iron Man", "Giant Man" }, "DEF decline on opposing Team", 0.85, 5, false, ComboTarget.Opposing, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCMasterminds", "Masterminds", new[] { "Doctor Doom", "Magneto", "Red Skull" }, "Strengthen ATK of your Villains", 0.85, 6, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Villain" });
            AddCombo("SCFamilialBonds", "Familial Bonds", new[] { "Cable", "Cyclops", "Jean Grey" }, "Strengthen ATK of Cable, Cyclops & Jean Grey", 0.85, 10, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Cable", "Cyclops", "Jean Grey" });
            AddCombo("SCAsgardianEminence", "Asgardian Eminence", new[] { "Thor", "Loki", "Odin" }, "Strengthen ATK of Thor, Loki & Odin", 0.85, 10, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Thor", "Loki", "Odin" });
            AddCombo("SCColdHearts", "Cold Hearts", new[] { "Maria Hill", "Punisher", "Psylocke" }, "Strengthen ATK of Maria, Punisher & Psylocke", 0.85, 10, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Maria Hill", "Punisher", "Psylocke" });
            AddCombo("SCEliteAgents", "Elite Agents", new[] { "Maria Hill", "Black Widow", "Nick Fury" }, "Strengthen ATK of Heroes", 0.85, 6, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Super Hero" });
            AddCombo("SCBrotherhoodTrio", "Brotherhood Trio", new[] { "Magneto", "Juggernaut", "Mystique" }, "Strengthen ATK of Magneto, Juggernaut & Mystique", 0.85, 10, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Magneto", "Juggernaut", "Mystique" });
            AddCombo("SCCatastrophe", "Catastrophe", new[] { "Green Goblin", "Vulture", "Morbius" }, "Strengthen ATK of your Speeds", 0.75, 10, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Speed" });
            AddCombo("SCFatalTriad", "Fatal Triad", new[] { "Thanos", "Death", "Gamora" }, "Strengthen ATK of Thanos, Death & Gamora", 0.85, 10, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Thanos", "Death", "Gamora" });
            AddCombo("SCTripleDespair", "Triple Despair", new[] { "Doctor Doom", "Death", "Mister Sinister" }, "Strengthen ATK of Tactics alignment", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Tactics" });
            AddCombo("SCSHIELDWomen", "SHIELD Women", new[] { "Maria Hill", "Black Widow", "Sharon Carter" }, "Strengthen ATK of Heroes", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Super Hero" });
            AddCombo("SCSoldierBoys", "Soldier Boys", new[] { "Nick Fury", "Captain America", "War Machine" }, "Strengthen ATK of your Team", 0.90, 4, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCFromTheCosmos", "From the Cosmos", new[] { "Thanos", "Silver Surfer", "Galactus" }, "Strengthen ATK of your Team", 0.90, 4, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("Regeneration", "Regeneration", new[] { "Omega Red", "Wolverine", "Luke Cage" }, "Strengthen ATK of Bruiser alignment", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Bruiser" });
            AddCombo("SCInterstellarThreat", "Interstellar Threat", new[] { "Venom", "Thanos", "Phoenix Force" }, "Strengthen ATK of Bruiser alignment", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Bruiser" });
            AddCombo("SCInterstellarAlliance", "Interstellar Alliance", new[] { "Drax", "Miek", "Groot" }, "Strengthen ATK of Bruiser alignment", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Bruiser" });
            AddCombo("SCArachnidTrio", "Arachnid Trio", new[] { "Spider-Man", "Spider-Girl", "Spider-Woman" }, "Strengthen ATK of Speed alignment", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Speed" });
            AddCombo("SCBeatingWings", "Beating Wings", new[] { "Angel", "Wasp", "Lockheed" }, "Strengthen ATK of Speed alignment", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Speed" });
            AddCombo("SCThreeHammers", "Three Hammers", new[] { "Thor", "Beta-Ray Bill", "Ragnarok" }, "Strengthen ATK of Bruiser alignment", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Bruiser" });
            AddCombo("SCRazorSharp", "Razor Sharp", new[] { "Psylocke", "Blade", "Deadpool" }, "Strengthen ATK of your Team", 0.90, 4, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCPetAvengers", "Pet Avengers", new[] { "Zabu", "Throg", "Lockjaw" }, "Strengthen ATK of your Heroes", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Super Hero" });
            AddCombo("SCMasterPlanners", "Master Planners", new[] { "Professor X", "Fantomex", "Mandarin" }, "Strengthen ATK of Tactics alignment", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Tactics" });

            // --------------------------------------------------
            // 3. 4-Card Attack Combos
            // --------------------------------------------------
            AddCombo("SCHomoSuperior", "Homo Superior", new[] { "Apocalypse", "Jean Grey", "Wolverine", "Cyclops" }, "Strengthen ATK of Bruiser alignment", 0.90, 7, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Bruiser" });
            AddCombo("SCCrackingWise", "Cracking Wise", new[] { "Spider-Man", "Rocket Raccoon", "Deadpool", "Ghost Rider" }, "Strengthen ATK of Speed alignment", 0.90, 7, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Alignment, new[] { "Speed" });
            AddCombo("SCSparkIgnition", "Spark Ignition", new[] { "Thor", "Storm", "Ghost Rider", "Human Torch" }, "Strengthen ATK of Thor, Storm, GR & Torch", 0.80, 7, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Thor", "Storm", "Ghost Rider", "Human Torch" });
            AddCombo("SCXForce", "X-Force", new[] { "Wolverine", "Domino", "Psylocke", "X-23" }, "Strengthen ATK of your Team", 0.90, 7, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCMidnightStrike", "Midnight Strike", new[] { "Nightcrawler", "Daredevil", "Ghost Rider", "Blade" }, "Strengthen ATK of your Team", 0.90, 7, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCHeroesforhire", "Heroes for Hire", new[] { "Punisher", "Iron Fist", "Ghost Rider", "Luke Cage" }, "Strengthen ATK of Heroes", 0.90, 7, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Super Hero" });
            AddCombo("SCAsgardianOnslaught", "Asgardian Onslaught", new[] { "Valkyrie", "Sif", "Odin", "Thor" }, "Strengthen ATK of Heroes", 0.90, 7, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Super Hero" });
            AddCombo("SCRoyalQuartet", "Royal Quartet", new[] { "Star-Lord", "Black Bolt", "Thor", "Black Panther" }, "Strengthen ATK of your Team", 0.90, 7, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCNYCJustice", "NYC Justice", new[] { "Daredevil", "Spider-Man", "Captain America", "Punisher" }, "Strengthen ATK of your Team", 0.90, 7, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCFathersAndSons", "Fathers And Sons", new[] { "Thanos", "Wolverine", "Thane", "Daken" }, "Strengthen ATK of your Team", 0.90, 7, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCMassiveFour", "Massive Four", new[] { "Omega Red", "Kingpin", "Juggernaut", "Rhino" }, "Strengthen ATK of your Villains", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Villain" });
            AddCombo("SCMightyTransformation", "Mighty Transformation", new[] { "Wonder Man", "Angel", "Colossus", "Kitty Pryde" }, "Degrade DEF of opposing Team", 0.90, 7, false, ComboTarget.Opposing, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCLookAtMyFriends", "Look At My Friends", new[] { "Thanos", "Thor", "Deadpool", "Iron Fist" }, "Strengthen ATK of your Team", 0.90, 7, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCMortalEnemies", "Mortal Enemies", new[] { "Sabretooth", "Wolverine", "Spider-Man", "Venom" }, "Strengthen ATK of your Villains", 0.80, 5, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Villain" });

            // --------------------------------------------------
            // 4. 5-Card Attack Combos
            // --------------------------------------------------
            AddWildcardCombo("SCDangerousBeauties", "Dangerous Beauties", "DEF decline on opposing team", 0.90, 4, false, ComboTarget.Opposing, ComboStat.Def, ComboScope.All, Array.Empty<string>(), fem: true, vil: false, her: false, mHer: false, mVil: false, same: false, "");
            AddWildcardCombo("SCDarkPowers", "Dark Powers", "DEF decline on opposing Team", 0.80, 3, false, ComboTarget.Opposing, ComboStat.Def, ComboScope.All, Array.Empty<string>(), fem: false, vil: true, her: false, mHer: false, mVil: false, same: false, "");
            AddWildcardCombo("SCGoPoundSand", "Go Pound Sand", "Strengthen ATK of Sandman", 0.80, 4, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Sandman" }, fem: false, vil: false, her: false, mHer: false, mVil: false, same: true, "Sandman");
            AddWildcardCombo("SCFuryBlast", "Fury Blast", "Strengthen ATK of your Heroes", 0.90, 3, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Super Hero" }, fem: false, vil: false, her: false, mHer: true, mVil: false, same: false, "");
            AddWildcardCombo("SCWildVillains", "Wild Villains", "Strengthen ATK of your Villains", 0.90, 3, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.Faction, new[] { "Villain" }, fem: false, vil: false, her: false, mHer: false, mVil: true, same: false, "");
            AddWildcardCombo("SCDestroyAndRebuild", "Destroy and Rebuild", "Strengthen ATK of Builder", 0.80, 4, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.SpecificCharacters, new[] { "Builder" }, fem: false, vil: false, her: false, mHer: false, mVil: false, same: true, "Builder");
            AddCombo("SCGuardiansOfTheGalaxy", "Guardians of the Galaxy", new[] { "Star-Lord", "Gamora", "Rocket Raccoon", "Drax", "Groot" }, "Strengthen ATK of your Team", 0.90, 9, false, ComboTarget.Friendly, ComboStat.Atk, ComboScope.All, Array.Empty<string>());

            // --------------------------------------------------
            // 5. 2-Card Defense Combos
            // --------------------------------------------------
            AddCombo("SCGammaBrothers", "Gamma Brothers", new[] { "Hulk", "A-Bomb" }, "DEF improvement on your Bruisers", 0.80, 5, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.Alignment, new[] { "Bruiser" });
            AddCombo("SCLawAndOrder", "Law and Order", new[] { "She-Hulk", "Iron Man" }, "Harden DEF of all Heroes", 0.80, 5, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.Faction, new[] { "Super Hero" });
            AddCombo("SCHopeForTheFuture", "Hope for the Future", new[] { "Cable", "Hope Summers" }, "Harden DEF of Cable & Hope Summers", 0.85, 13, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.SpecificCharacters, new[] { "Cable", "Hope Summers" });
            AddCombo("SCManMachine", "Man & Machine", new[] { "Captain America", "Vision" }, "Harden DEF of your Tactics", 0.80, 5, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.Alignment, new[] { "Tactics" });
            AddCombo("SCRebelliousFists", "Rebellious Fists", new[] { "Captain America", "Hercules" }, "Degrade ATK of opposing team", 0.80, 5, true, ComboTarget.Opposing, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCSynthezoidPower", "Synthezoid Power", new[] { "Vision", "Apocalypse" }, "Harden DEF of your Tactics", 0.80, 5, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.Alignment, new[] { "Tactics" });
            AddCombo("SCWhatARoughBeast", "What a Rough Beast", new[] { "Beast", "Apocalypse" }, "Harden DEF of your Bruisers", 0.80, 5, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.Alignment, new[] { "Bruiser" });
            AddCombo("SCWeltuntergang", "Weltuntergang", new[] { "Nightcrawler", "Apocalypse" }, "Weaken ATK of opposing Heroes", 0.80, 6, true, ComboTarget.Opposing, ComboStat.Atk, ComboScope.Faction, new[] { "Super Hero" });
            AddCombo("SCNexusField", "Nexus Field", new[] { "Scarlet Witch", "Apocalypse" }, "Degrade ATK of opposing team", 0.85, 4, true, ComboTarget.Opposing, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCMasterManipulators", "Master Manipulators", new[] { "Supergiant", "Ebony Maw" }, "Degrade ATK of opposing Heroes", 0.80, 5, true, ComboTarget.Opposing, ComboStat.Atk, ComboScope.Faction, new[] { "Super Hero" });

            // --------------------------------------------------
            // 6. 3-Card Defense Combos
            // --------------------------------------------------
            AddCombo("SCGammaPowers", "Gamma Powers", new[] { "Hulk", "A-Bomb", "Skaar" }, "DEF improvement on your Bruisers", 0.85, 6, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.Alignment, new[] { "Bruiser" });
            AddCombo("SCTheRichards", "The Richards", new[] { "Mr. Fantastic", "Invisible Woman", "Franklin" }, "DEF improvement on your team", 0.80, 10, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCDefenders", "Defenders", new[] { "Silver Surfer", "Hulk", "Doctor Strange" }, "DEF improvement on your Heroes", 0.80, 12, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.Faction, new[] { "Super Hero" });
            AddCombo("SCHealingFactor", "Healing Factor", new[] { "X-23", "Wolverine", "Deadpool" }, "Harden DEF of your team", 0.85, 6, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.All, Array.Empty<string>());

            // --------------------------------------------------
            // 7. 4-Card Defense Combos
            // --------------------------------------------------
            AddCombo("SCFantasticFour", "Fantastic Four", new[] { "Mr. Fantastic", "Invisible Woman", "The Thing", "Human Torch" }, "ATK decline on opposing team", 0.80, 10, true, ComboTarget.Opposing, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCFulltimeWorkers", "Full-time Workers", new[] { "Iron Man", "Doctor Strange", "Punisher", "She-Hulk" }, "DEF Improvement on your team", 0.80, 10, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCBraveheart", "Braveheart", new[] { "Thor", "Daredevil", "Captain America", "Spider-Man" }, "DEF improvement on Thor, Daredevil, Cap & Spidey", 0.80, 7, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.SpecificCharacters, new[] { "Thor", "Daredevil", "Captain America", "Spider-Man" });
            AddCombo("SCImpenetrable", "Impenetrable", new[] { "Hercules", "The Thing", "Hulk", "Colossus" }, "DEF improvement on your Bruisers", 0.90, 6, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.Alignment, new[] { "Bruiser" });
            AddCombo("SCMightOfEvil", "Might of Evil", new[] { "Ronan", "Ultron", "Red Skull", "Mister Sinister" }, "Degrade ATK of opposing team", 0.90, 7, true, ComboTarget.Opposing, ComboStat.Atk, ComboScope.All, Array.Empty<string>());
            AddCombo("SCCyborgCreation", "Cyborg Creation", new[] { "Mr. Fantastic", "Iron Man", "Giant Man", "Ragnarok" }, "DEF Improvement on your team", 0.80, 10, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCColorShield", "Color Shield", new[] { "Black Bolt", "Quicksilver", "Silver Surfer", "Scarlet Witch" }, "DEF Improvement on your team", 0.80, 10, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCUnbreakableWomen", "Unbreakable Women", new[] { "Invisible Woman", "Scarlet Witch", "Emma Frost", "Death" }, "Harden DEF of your team", 0.80, 10, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCBigScience", "Big Science", new[] { "Beast", "Black Bolt", "Mister Sinister", "Doctor Spectrum" }, "Harden DEF of your team", 0.80, 10, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCAssemble", "Assemble", new[] { "Hawkeye", "Vision", "Iron Man", "Captain America" }, "Harden DEF of your team", 0.80, 10, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.All, Array.Empty<string>());
            AddCombo("SCGreatConspirators", "Great Conspirators", new[] { "Mister Sinister", "Red Skull", "Magneto", "MODOK" }, "Harden DEF of your team", 0.80, 10, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.All, Array.Empty<string>());

            // --------------------------------------------------
            // 8. 5-Card Defense Combos
            // --------------------------------------------------
            AddWildcardCombo("SCMarvelHeroTeam", "Marvel Hero Team", "DEF improvement on your team", 0.80, 3, true, ComboTarget.Friendly, ComboStat.Def, ComboScope.All, Array.Empty<string>(), fem: false, vil: false, her: true, mHer: false, mVil: false, same: false, "");
            AddWildcardCombo("SCEvilAlliance", "Evil Alliance", "Degrade ATK of opposing team", 0.85, 3, true, ComboTarget.Opposing, ComboStat.Atk, ComboScope.All, Array.Empty<string>(), fem: false, vil: true, her: false, mHer: false, mVil: false, same: false, "");
            AddWildcardCombo("SCGlamorousGuardians", "Glamorous Guardians", "Degrade ATK of opposing team", 0.80, 3, true, ComboTarget.Opposing, ComboStat.Atk, ComboScope.All, Array.Empty<string>(), fem: true, vil: false, her: false, mHer: false, mVil: false, same: false, "");
        }
    }
}
