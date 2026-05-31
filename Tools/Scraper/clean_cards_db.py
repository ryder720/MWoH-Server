import os
import json

RARITY_ORDER = [
    "Common",
    "Uncommon",
    "Rare",
    "Special Rare",
    "Super Special Rare",
    "Ultimate Rare",
    "Legendary",
    "Ultimate Legendary"
]

def get_promoted_rarity(base_rarity):
    if not base_rarity:
        return "Common"
    # Match standard rarity names case-insensitively
    matched = None
    for r in RARITY_ORDER:
        if r.lower() == base_rarity.strip().lower():
            matched = r
            break
            
    if not matched:
        # Fallback names
        if base_rarity.strip().lower() in ["normal"]:
            matched = "Common"
        elif base_rarity.strip().lower() in ["high normal"]:
            matched = "Uncommon"
        elif base_rarity.strip().lower() in ["high rare"]:
            matched = "Special Rare"
        elif base_rarity.strip().lower() in ["super rare"]:
            matched = "Super Special Rare"
        elif base_rarity.strip().lower() in ["ultra rare"]:
            matched = "Ultimate Rare"
        elif base_rarity.strip().lower() in ["legend"]:
            matched = "Legendary"
        elif base_rarity.strip().lower() in ["special legend"]:
            matched = "Ultimate Legendary"
        else:
            return base_rarity # Keep as is if unrecognized

    idx = RARITY_ORDER.index(matched)
    if idx < len(RARITY_ORDER) - 1:
        return RARITY_ORDER[idx + 1]
    return matched # Already at max tier

def main():
    json_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "cards_db.json")
    if not os.path.exists(json_path):
        print(f"Error: cards_db.json not found at {json_path}")
        return

    with open(json_path, "r", encoding="utf-8") as f:
        cards = json.load(f)

    cleaned_cards = []
    removed_count = 0
    resolved_count = 0

    for card in cards:
        title = card.get("title", "")
        # Remove broken stub card
        if title == "Nexus Star-Lord":
            removed_count += 1
            print(f"Removing stub card: {title}")
            continue

        variants = card.get("variants", {})
        
        # 1. Determine base card's standard rarity
        base_rarity = None
        for var_name, var_data in variants.items():
            rarity = var_data.get("rarity")
            if rarity and rarity not in ["Unknown", "MISSING", "?", "None", None] and rarity.strip():
                # Prefer base variant's rarity
                if "+" not in var_name:
                    base_rarity = rarity
                    break

        # Fallback if no non-plus variant has a valid rarity
        if not base_rarity:
            for var_name, var_data in variants.items():
                rarity = var_data.get("rarity")
                if rarity and rarity not in ["Unknown", "MISSING", "?", "None", None] and rarity.strip():
                    base_rarity = rarity
                    break

        # 2. Fix variants with anomalous rarities
        for var_name, var_data in list(variants.items()):
            rarity = var_data.get("rarity")
            if rarity in ["Unknown", "MISSING", "?", "None", None] or not rarity.strip():
                if base_rarity:
                    if "+" in var_name:
                        new_rarity = get_promoted_rarity(base_rarity)
                        var_data["rarity"] = new_rarity
                        resolved_count += 1
                        print(f"Resolved (Promoted): {title} | {var_name} -> {new_rarity} (base: {base_rarity})")
                    else:
                        var_data["rarity"] = base_rarity
                        resolved_count += 1
                        print(f"Resolved (Inherited): {title} | {var_name} -> {base_rarity}")
                else:
                    # Default absolute fallback if base rarity is also missing
                    var_data["rarity"] = "Common"
                    resolved_count += 1
                    print(f"Resolved (Default): {title} | {var_name} -> Common")

        cleaned_cards.append(card)

    # Save cleaned cards database back
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(cleaned_cards, f, indent=4, ensure_ascii=False)

    print("\nSanitization Summary:")
    print(f"  Removed stubs: {removed_count}")
    print(f"  Resolved anomalies: {resolved_count}")
    print(f"  Saved {len(cleaned_cards)} cards back to cards_db.json successfully!")

if __name__ == "__main__":
    main()
