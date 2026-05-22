import os
import re
import json

def clean_html(text):
    text = re.sub(r'<[^>]+>', '', text)
    text = text.replace("&nbsp;", " ").replace("&amp;", "&").replace("&quot;", '"').replace("&ldquo;", '"').replace("&rdquo;", '"')
    return re.sub(r'\s+', ' ', text).strip()

def main():
    # The scraped page from Fandom wiki is cached in content.md during session
    filepath = r"C:\Users\Ryder\.gemini\antigravity\brain\0122896f-925b-49f3-ab45-26aa464b3363\.system_generated\steps\3576\content.md"
    if not os.path.exists(filepath):
        print(f"Historical content file not found: {filepath}")
        return

    with open(filepath, "r", encoding="utf-8") as f:
        content = f.read()

    # Find all h3 headings which represent individual items
    h3_pattern = re.compile(r'<h3><span class="mw-headline"[^>]*>(.*?)</span></h3>', re.DOTALL)
    
    headings = []
    for match in h3_pattern.finditer(content):
        headings.append({
            "name": clean_html(match.group(1)),
            "start": match.start(),
            "end": match.end()
        })
    
    print(f"Analyzing {len(headings)} item entities from MWoH Fandom Wiki...")

    items_db = []
    
    for i, heading in enumerate(headings):
        start_pos = heading["end"]
        end_pos = headings[i+1]["start"] if i + 1 < len(headings) else len(content)
        segment = content[start_pos:end_pos]
        
        # Extract Image URL
        img_match = re.search(r'href="(https://static\.wikia\.nocookie\.net/marvel-war-of-heroes/images/[^"]+)"', segment)
        img_url = ""
        if img_match:
            img_url = img_match.group(1).split('/revision/')[0]
        
        # Extract Paragraph text
        p_pattern = re.compile(r'<p>(.*?)</p>', re.DOTALL)
        paragraphs = [clean_html(p) for p in p_pattern.findall(segment)]
        paragraphs = [p for p in paragraphs if p and not p.startswith("File:") and not p.startswith("This page is about")]
        description = " ".join(paragraphs).strip()
        
        # Assign dynamic categories and restorative effect values
        name_lower = heading["name"].lower()
        item_type = "General"
        effect_value = 0
        
        if "energy" in name_lower or "stamina" in name_lower:
            item_type = "EnergyRestorative"
            effect_value = 50 if "half" in name_lower else 100
        elif "attack" in name_lower or "battle power" in name_lower or "bp" in name_lower or "power pack" in name_lower:
            item_type = "AttackPowerRestorative"
            effect_value = 50 if "half" in name_lower else 100
        elif "defense" in name_lower:
            item_type = "DefensePowerRestorative"
            effect_value = 50 if "half" in name_lower else 100
        elif "mastery" in name_lower:
            item_type = "MasteryIso8"
            effect_value = 10
            
        items_db.append({
            "id": i + 1,
            "name": heading["name"],
            "description": description,
            "type": item_type,
            "effect_value": effect_value,
            "image_url": img_url
        })
        
    out_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "items_db.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(items_db, f, indent=4)
        
    print(f"Scraped and cataloged {len(items_db)} items into {out_path}")

if __name__ == "__main__":
    main()
