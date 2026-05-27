import os
import re
import json
import urllib.request
import urllib.parse

def clean_html(text):
    text = re.sub(r'<[^>]+>', '', text)
    text = text.replace("&nbsp;", " ").replace("&amp;", "&").replace("&quot;", '"').replace("&ldquo;", '"').replace("&rdquo;", '"')
    return re.sub(r'\s+', ' ', text).strip()

def main():
    print("[*] Fetching Items page from MWoH Fandom Wiki...")
    url = "https://marvel-war-of-heroes.fandom.com/api.php?action=parse&page=Items&format=json"
    req = urllib.request.Request(
        url,
        headers={
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36',
        }
    )
    try:
        with urllib.request.urlopen(req) as response:
            res_data = json.loads(response.read().decode('utf-8'))
            if "parse" in res_data:
                content = res_data["parse"].get("text", {}).get("*", "")
            else:
                print("[-] ERROR: Failed to parse Items page from Wiki API response.")
                return
    except Exception as e:
        print(f"[-] ERROR: Failed to fetch Items page: {e}")
        return

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
