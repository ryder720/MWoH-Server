import os
import re
import sys
import json
import time
import urllib.request
import urllib.parse
from html.parser import HTMLParser

# Scraper configuration
BASE_WIKI_URL = "https://marvel-war-of-heroes.fandom.com"
ALIGNMENTS = ["Speed", "Bruiser", "Tactics"]
DATA_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_JSON = os.path.join(DATA_DIR, "cards_db.json")
IMAGE_DIR = os.path.join(DATA_DIR, "illustrations")

# Cooldown to respect server load (in seconds)
REQUEST_DELAY = 1.5

class WikiaInfoboxParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.in_table = False
        self.current_tag = None
        self.current_attrs = {}
        self.captured_text = []
        self.table_depth = 0
        
        # Intermediate structure
        self.raw_html_rows = []
        self.current_row = []
        self.in_row = False
        self.in_cell = False
        self.cell_tag = None
        self.cell_attrs = {}

    def handle_starttag(self, tag, attrs):
        attrs_dict = dict(attrs)
        if tag == "table" and "wikia-infobox" in attrs_dict.get("class", ""):
            self.in_table = True
            self.table_depth += 1
        elif self.in_table:
            if tag == "table":
                self.table_depth += 1
            elif tag == "tr":
                self.in_row = True
                self.current_row = []
            elif tag in ["td", "th"]:
                self.in_cell = True
                self.cell_tag = tag
                self.cell_attrs = attrs_dict
                self.captured_text = []
            elif tag in ["a", "img"] and self.in_cell:
                # Capture variant hint from title or alt attributes
                title_attr = attrs_dict.get("title") or attrs_dict.get("alt")
                if title_attr and title_attr.strip():
                    self.cell_attrs["variant_hint"] = title_attr.strip()

    def handle_endtag(self, tag):
        if self.in_table:
            if tag == "table":
                self.table_depth -= 1
                if self.table_depth == 0:
                    self.in_table = False
            elif tag == "tr":
                self.in_row = False
                self.raw_html_rows.append(self.current_row)
            elif tag in ["td", "th"] and self.in_cell:
                text = " ".join(self.captured_text).strip()
                # Clean up multiple whitespaces
                text = re.sub(r'\s+', ' ', text)
                self.current_row.append({
                    "tag": self.cell_tag,
                    "attrs": self.cell_attrs,
                    "text": text,
                    "variant_hint": self.cell_attrs.get("variant_hint")
                })
                self.in_cell = False

    def handle_data(self, data):
        if self.in_table and self.in_cell:
            self.captured_text.append(data)

def fetch_wiki_api(params):
    """Safely queries MediaWiki API with Chrome-like user headers."""
    params["format"] = "json"
    query_string = urllib.parse.urlencode(params)
    url = f"{BASE_WIKI_URL}/api.php?{query_string}"
    
    req = urllib.request.Request(
        url,
        headers={
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36',
        }
    )
    
    try:
        time.sleep(REQUEST_DELAY) # Strict throttling to avoid IP bans
        with urllib.request.urlopen(req) as response:
            return json.loads(response.read().decode('utf-8'))
    except Exception as e:
        print(f"Error fetching API {url}: {e}")
        return None

def fetch_page_html(title):
    """Fetches parsed HTML text for a specific wiki page."""
    encoded_title = urllib.parse.quote(title)
    url = f"{BASE_WIKI_URL}/api.php?action=parse&page={encoded_title}&format=json"
    
    req = urllib.request.Request(
        url,
        headers={
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36',
        }
    )
    
    try:
        time.sleep(REQUEST_DELAY) # Strict throttling
        with urllib.request.urlopen(req) as response:
            res_data = json.loads(response.read().decode('utf-8'))
            if "parse" in res_data:
                return res_data["parse"].get("text", {}).get("*", "")
    except Exception as e:
        print(f"Error parsing page {title}: {e}")
    return None

def extract_image_urls(html):
    """Regex-based helper to grab static wikia images from the page text."""
    urls = re.findall(r'href="(https://static.wikia.nocookie.net/marvel-war-of-heroes/images/[^"]+)"', html)
    cleaned_urls = []
    for u in urls:
        # Clean scale path parameters to get raw original image
        clean_url = u.split('/revision/')[0]
        revision_part = re.search(r'(/revision/latest\?cb=\d+)', u)
        if revision_part:
            clean_url += revision_part.group(1)
        else:
            clean_url += "/revision/latest"
            
        if clean_url not in cleaned_urls:
            cleaned_urls.append(clean_url)
    return cleaned_urls

def parse_card_page(title, alignment):
    """Aggregates image extraction, metadata mining, and stats mapping."""
    html = fetch_page_html(title)
    if not html:
        return None
        
    print(f"Parsing card: {title} ({alignment})")
    
    # Extract original high-quality card art URLs
    img_urls = extract_image_urls(html)
    
    # Setup html table parser
    parser = WikiaInfoboxParser()
    parser.feed(html)
    
    card_data = {
        "title": title,
        "alignment": alignment,
        "images": img_urls,
        "variants": {},
        "general": {}
    }
    
    current_variant = None
    
    rows = parser.raw_html_rows
    for row in rows:
        if not row:
            continue
            
        # Update current variant from variant hint if present in the row
        for cell in row:
            hint = cell.get("variant_hint")
            if hint:
                if not any(ext in hint.lower() for ext in [".jpg", ".png", ".gif", "thumb"]):
                    current_variant = hint
                    if current_variant not in card_data["variants"]:
                        card_data["variants"][current_variant] = {"stats": {}}
            
            # Support fallback caption cell as well
            if "infobox-caption" in cell["attrs"].get("class", ""):
                cap_text = cell["text"].strip()
                if cap_text and not any(ext in cap_text.lower() for ext in [".jpg", ".png", ".gif", "thumb"]):
                    current_variant = cap_text
                    if current_variant not in card_data["variants"]:
                        card_data["variants"][current_variant] = {"stats": {}}
            
        # Parse titles
        if len(row) == 1 and row[0]["tag"] == "th" and "infobox-header" in row[0]["attrs"].get("class", ""):
            visual_title = row[0]["text"].strip()
            card_data["visual_title"] = visual_title
            if not current_variant:
                current_variant = visual_title
                if current_variant not in card_data["variants"]:
                    card_data["variants"][current_variant] = {"stats": {}}
            continue
            
        if not current_variant:
            current_variant = title
            if current_variant not in card_data["variants"]:
                card_data["variants"][current_variant] = {"stats": {}}
                
        # Standard Key-Value headers
        if len(row) >= 2:
            key = row[0]["text"].strip()
            val = row[1]["text"].strip()
            
            is_general = any(g_key in key for g_key in ["Alignment", "Gender", "Faction", "First Release Date"])
            
            if is_general:
                clean_key = key.lower().replace(" ", "_")
                card_data["general"][clean_key] = val
            else:
                var_dict = card_data["variants"][current_variant]
                if "Rarity" in key:
                    var_dict["rarity"] = val
                elif "Power Requirement" in key:
                    try:
                        var_dict["power"] = int(val)
                    except:
                        var_dict["power"] = val
                elif "Sale Price" in key:
                    var_dict["price"] = val
                elif "Maximum Card Level" in key:
                    var_dict["max_level"] = val
                elif "Maximum Mastery Level" in key:
                    var_dict["max_mastery"] = val
                elif "Quote" in key:
                    var_dict["quote"] = val
                elif "Ability" in key:
                    card_data["general"]["ability_name"] = val
                elif "Effect" in key:
                    card_data["general"]["ability_effect"] = val
                    
        # Stats extraction row by row
        has_stats_header = False
        stats_type = None
        for cell in row:
            if cell["tag"] == "th":
                txt = cell["text"].lower()
                if any(s_kwd in txt for s_kwd in ["base", "maximum", "mastery", "proper", "catalog"]):
                    has_stats_header = True
                    stats_type = txt
                    break
                    
        if has_stats_header:
            numeric_vals = []
            for cell in row:
                if cell["tag"] == "td":
                    clean_txt = cell["text"].replace(",", "").strip()
                    if clean_txt.isdigit():
                        numeric_vals.append(int(clean_txt))
            
            if len(numeric_vals) >= 2:
                atk, def_val = numeric_vals[0], numeric_vals[1]
                prefix = ""
                if "proper" in stats_type:
                    prefix = "proper_fused_"
                elif "catalog" in stats_type:
                    prefix = "catalog_"
                elif "mastery" in stats_type:
                    prefix = "mastery_bonus_"
                elif "maximum" in stats_type:
                    prefix = "max_"
                elif "base" in stats_type:
                    prefix = "base_"
                
                if prefix:
                    var_dict = card_data["variants"][current_variant]
                    var_dict["stats"][prefix + "atk"] = atk
                    var_dict["stats"][prefix + "def"] = def_val
                    
    return card_data

def download_image(url, filename):
    """Downloads card art from Wiki CDN into local directories."""
    path = os.path.join(IMAGE_DIR, filename)
    if os.path.exists(path):
        return True # Skip duplicate
        
    req = urllib.request.Request(
        url,
        headers={
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36',
        }
    )
    try:
        time.sleep(REQUEST_DELAY) # Strict throttling
        with urllib.request.urlopen(req) as response:
            with open(path, "wb") as f:
                f.write(response.read())
        print(f"Downloaded image: {filename}")
        return True
    except Exception as e:
        print(f"Error downloading image {url}: {e}")
        return False

def main():
    os.makedirs(IMAGE_DIR, exist_ok=True)
    
    # Load existing progress
    if os.path.exists(OUTPUT_JSON):
        with open(OUTPUT_JSON, "r", encoding="utf-8") as f:
            scraped_cards = json.load(f)
    else:
        scraped_cards = []
        
    scraped_titles = {c["title"] for c in scraped_cards}
    print(f"Loaded {len(scraped_cards)} existing cards from cache.")
    
    # Determine if running in Sandbox Mode (only scrape 3 cards per alignment)
    is_sandbox = "--sandbox" in sys.argv
    if is_sandbox:
        print("WARNING: Sandbox Mode activated! Will only crawl up to 3 cards per alignment.")
        
    for alignment in ALIGNMENTS:
        print(f"\n=== Commencing category member scan: {alignment} ===")
        cmcontinue = None
        alignment_scraped_count = 0
        
        while True:
            params = {
                "action": "query",
                "list": "categorymembers",
                "cmtitle": f"Category:{alignment}",
                "cmlimit": "100"
            }
            if cmcontinue:
                params["cmcontinue"] = cmcontinue
                
            res = fetch_wiki_api(params)
            if not res or "query" not in res:
                break
                
            members = res["query"].get("categorymembers", [])
            for member in members:
                if is_sandbox and alignment_scraped_count >= 3:
                    break
                    
                title = member["title"]
                if title in scraped_titles:
                    if is_sandbox:
                        alignment_scraped_count += 1
                    continue
                    
                # Exclude administrative pages, directories or general lists
                if any(kwd in title for kwd in ["Category:", "File:", "Template:", "Talk:", "Wiki:"]):
                    continue
                    
                try:
                    card = parse_card_page(title, alignment)
                    if card and any("rarity" in var for var in card["variants"].values()): # Valid card check
                        scraped_cards.append(card)
                        scraped_titles.add(title)
                        alignment_scraped_count += 1
                        
                        # Save incrementally to protect progress!
                        with open(OUTPUT_JSON, "w", encoding="utf-8") as f:
                            json.dump(scraped_cards, f, indent=4, ensure_ascii=False)
                            
                        # Download related card images
                        for idx, img_url in enumerate(card["images"]):
                            safe_title = "".join([c if c.isalnum() else "_" for c in title])
                            filename = f"{safe_title}_{idx+1}.jpg"
                            download_image(img_url, filename)
                            
                except Exception as ex:
                    print(f"Failed to scrape card '{title}': {ex}")
                    
            if is_sandbox and alignment_scraped_count >= 3:
                print(f"Sandbox limit met for {alignment}.")
                break
                
            if "continue" in res and "cmcontinue" in res["continue"]:
                cmcontinue = res["continue"]["cmcontinue"]
            else:
                break
                
    print("\nScrape cycle complete! Total cards compiled:", len(scraped_cards))

if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--test":
        test_title = "Great Responsibility Spider-Man"
        if len(sys.argv) > 2:
            test_title = sys.argv[2]
        
        print(f"Running single-card test for: {test_title}")
        card = parse_card_page(test_title, "Speed")
        if card:
            print("\nSUCCESS! Structured Card Data Extracted:")
            print(json.dumps(card, indent=4, ensure_ascii=False))
        else:
            print("Failed to fetch or parse card.")
    else:
        main()
