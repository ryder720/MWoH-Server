import os
import sys
import json
import urllib.request
import urllib.parse
import time

def main():
    scraper_dir = os.path.dirname(os.path.abspath(__file__))
    cards_db_path = os.path.join(scraper_dir, "cards_db.json")
    
    if not os.path.exists(cards_db_path):
        print(f"Error: cards_db.json not found at '{cards_db_path}'")
        return
        
    wwwroot_dir = os.path.abspath(os.path.join(scraper_dir, "..", "..", "Server", "wwwroot", "images", "cards"))
    scraper_illustrations_dir = os.path.join(scraper_dir, "illustrations")
    
    os.makedirs(wwwroot_dir, exist_ok=True)
    os.makedirs(scraper_illustrations_dir, exist_ok=True)
    
    print("[Downloader] Reading cards database...")
    with open(cards_db_path, "r", encoding="utf-8") as f:
        cards = json.load(f)
        
    missing_count = 0
    downloaded_count = 0
    
    headers = {
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36'
    }
    
    print("[Downloader] Scanning for missing card illustrations...")
    for card in cards:
        title = card.get("title", "")
        if not title:
            continue
            
        safe_title = "".join([c if c.isalnum() else "_" for c in title])
        images = card.get("images", [])
        
        for idx, img_url in enumerate(images):
            filename = f"{safe_title}_{idx+1}.jpg"
            target_path = os.path.join(wwwroot_dir, filename)
            scraper_path = os.path.join(scraper_illustrations_dir, filename)
            
            # Check if missing
            if not os.path.exists(target_path) or not os.path.exists(scraper_path):
                missing_count += 1
                print(f"  [MISSING] {filename} -> Fetching from: {img_url}")
                
                try:
                    req = urllib.request.Request(img_url, headers=headers)
                    with urllib.request.urlopen(req) as response:
                        img_data = response.read()
                        
                    # Save to server wwwroot
                    with open(target_path, "wb") as f_out:
                        f_out.write(img_data)
                        
                    # Save to scraper illustrations folder to sync
                    with open(scraper_path, "wb") as f_out:
                        f_out.write(img_data)
                        
                    print(f"  [SUCCESS] Downloaded and cached {filename}")
                    downloaded_count += 1
                    
                    # Mild throttle to respect Wikia server load
                    time.sleep(1.0)
                except Exception as ex:
                    print(f"  [ERROR] Failed to download {filename}: {ex}")
                    
    print(f"\n[Downloader] Run complete! Missing detected: {missing_count}, Successfully downloaded: {downloaded_count}")

if __name__ == "__main__":
    main()
