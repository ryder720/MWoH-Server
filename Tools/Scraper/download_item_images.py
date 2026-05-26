import os
import sys
import json
import urllib.request
import urllib.parse
import time

def main():
    scraper_dir = os.path.dirname(os.path.abspath(__file__))
    items_db_path = os.path.join(scraper_dir, "items_db.json")
    
    if not os.path.exists(items_db_path):
        print(f"Error: items_db.json not found at '{items_db_path}'")
        return
        
    wwwroot_dir = os.path.abspath(os.path.join(scraper_dir, "..", "..", "Server", "wwwroot", "images", "items"))
    scraper_cache_dir = os.path.join(scraper_dir, "items")
    
    os.makedirs(wwwroot_dir, exist_ok=True)
    os.makedirs(scraper_cache_dir, exist_ok=True)
    
    print("[Downloader] Reading items database...")
    with open(items_db_path, "r", encoding="utf-8") as f:
        items = json.load(f)
        
    missing_count = 0
    downloaded_count = 0
    
    headers = {
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36'
    }
    
    print("[Downloader] Scanning for missing item illustrations...")
    for item in items:
        name = item.get("name", "")
        img_url = item.get("image_url", "")
        
        if not name or not img_url:
            continue
            
        try:
            uri = urllib.parse.urlparse(img_url)
            filename = os.path.basename(uri.path)
        except:
            filename = ""
            
        if not filename:
            filename = f"{name.replace(' ', '_')}.jpg"
            
        target_path = os.path.join(wwwroot_dir, filename)
        scraper_path = os.path.join(scraper_cache_dir, filename)
        
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
                    
                # Save to scraper cache folder to sync
                with open(scraper_path, "wb") as f_out:
                    f_out.write(img_data)
                    
                print(f"  [SUCCESS] Downloaded and cached {filename}")
                downloaded_count += 1
                
                # Mild throttle to respect Wikia server load
                time.sleep(0.5)
            except Exception as ex:
                print(f"  [ERROR] Failed to download {filename}: {ex}")
                
    print(f"\n[Downloader] Run complete! Missing detected: {missing_count}, Successfully downloaded: {downloaded_count}")

if __name__ == "__main__":
    main()
