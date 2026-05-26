import os
import re
import urllib.request
import urllib.parse
import html
import time

def clean_filename(name):
    # e.g. "Storm's cape red.jpg" -> "Storms_cape_red.jpg"
    name = html.unescape(name)
    name = name.replace("'", "").replace("&#039;", "").replace("&#39;", "")
    name = re.sub(r'[^a-zA-Z0-9_.-]', '_', name)
    # Replace multiple underscores with a single one
    name = re.sub(r'_{2,}', '_', name)
    return name

def main():
    scraper_dir = os.path.dirname(os.path.abspath(__file__))
    content_path = os.path.join(scraper_dir, "..", "..", "C:\\Users\\Ryder\\.gemini\\antigravity\brain\\21fb657b-58f5-4c68-8c42-4389d19001ad\\.system_generated\\steps\\947\\content.md")
    
    # Absolute path verification
    if not os.path.exists(content_path):
        # Fallback to direct path
        content_path = r"C:\Users\Ryder\.gemini\antigravity\brain\21fb657b-58f5-4c68-8c42-4389d19001ad\.system_generated\steps\947\content.md"
        
    if not os.path.exists(content_path):
        print(f"Error: content.md not found at '{content_path}'")
        return
        
    wwwroot_dir = os.path.abspath(os.path.join(scraper_dir, "..", "..", "Server", "wwwroot", "images", "items"))
    os.makedirs(wwwroot_dir, exist_ok=True)
    
    print("[Resources Downloader] Reading scraped wiki HTML content...")
    with open(content_path, "r", encoding="utf-8") as f:
        html_content = f.read()
        
    # Regex to find img tags with alt and src
    # E.g. <img src="https://..." alt="Storm&#039;s Red Cape" ...> or similar
    # Alt could come before src or vice versa. Let's do a robust search.
    img_tags = re.findall(r'<img([^>]+)>', html_content)
    print(f"[Resources Downloader] Found {len(img_tags)} img tags in document.")
    
    downloads = {}
    
    for tag in img_tags:
        src_match = re.search(r'src="([^"]+)"', tag)
        alt_match = re.search(r'alt="([^"]+)"', tag)
        
        if src_match and alt_match:
            src = src_match.group(1)
            alt = alt_match.group(1)
            
            # Ignore base64 images
            if src.startswith("data:"):
                continue
                
            # Clean Alt text
            alt_clean = html.unescape(alt)
            # Remove any HTML tags or weird entities
            alt_clean = re.sub(r'<[^>]+>', '', alt_clean)
            
            # Skip if alt is too generic (like "Resources" or empty)
            if not alt_clean or alt_clean.lower() in ["resources", "fandom", "wikia", "live content"]:
                continue
                
            # Parse direct url
            # e.g., https://static.wikia.nocookie.net/marvel-war-of-heroes/images/1/15/Storm%27s_cape_red.jpg/revision/latest?cb=20121101000116
            # We want to extract the clean filename and direct url (without /revision/...)
            clean_url = src
            rev_idx = src.find("/revision/latest")
            if rev_idx != -1:
                clean_url = src[:rev_idx]
                
            # Determine extension
            ext = ".jpg"
            if ".png" in clean_url.lower():
                ext = ".png"
            elif ".gif" in clean_url.lower():
                ext = ".gif"
                
            filename = clean_filename(alt_clean) + ext
            downloads[filename] = clean_url
            
    print(f"[Resources Downloader] Found {len(downloads)} unique resources to download.")
    
    headers = {
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36'
    }
    
    downloaded_count = 0
    for filename, url in downloads.items():
        target_path = os.path.join(wwwroot_dir, filename)
        
        if os.path.exists(target_path):
            print(f"  [EXISTS] {filename} is already cached.")
            continue
            
        print(f"  [DOWNLOADING] {filename} -> {url}")
        try:
            req = urllib.request.Request(url, headers=headers)
            with urllib.request.urlopen(req) as response:
                img_data = response.read()
                
            with open(target_path, "wb") as f_out:
                f_out.write(img_data)
                
            print(f"  [SUCCESS] Saved {filename}")
            downloaded_count += 1
            time.sleep(0.5)
        except Exception as ex:
            print(f"  [ERROR] Failed to download {filename}: {ex}")
            
    print(f"\n[Resources Downloader] Run complete! Downloaded: {downloaded_count} new images.")

if __name__ == "__main__":
    main()
