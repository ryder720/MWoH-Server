import os
import sys
import subprocess
import time
import argparse

# Reconfigure output streams to handle rich unicode characters on Windows
try:
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    if hasattr(sys.stderr, 'reconfigure'):
        sys.stderr.reconfigure(encoding='utf-8')
except Exception:
    pass

# Color palette for premium terminal aesthetics
COLOR_GREEN = "\033[92m"
COLOR_CYAN = "\033[96m"
COLOR_YELLOW = "\033[93m"
COLOR_RED = "\033[91m"
COLOR_BOLD = "\033[1m"
COLOR_RESET = "\033[0m"

# Handle platforms that don't support ANSI colors by default (e.g. standard cmd on old Windows versions)
if sys.platform == "win32":
    try:
        os.system("") # Enables ANSI escape sequences in Windows Command Prompt
    except Exception:
        pass

BANNER = f"""
{COLOR_CYAN}┌───────────────────────────────────────────────────────────┐
│        {COLOR_BOLD}S.H.I.E.L.D. TACTICAL DATA SCRAPER HARVESTER{COLOR_RESET}{COLOR_CYAN}       │
│               [MWoH PRIVATE SERVER UTILITY]               │
└───────────────────────────────────────────────────────────┘{COLOR_RESET}
"""

def print_section_header(title):
    print(f"\n{COLOR_CYAN}{COLOR_BOLD}>> [SYSTEM] {title} {COLOR_RESET}")
    print(f"{COLOR_CYAN}─────────────────────────────────────────────────────────────{COLOR_RESET}")

def run_scraper_subprocess(script_name, args=[]):
    script_dir = os.path.dirname(os.path.abspath(__file__))
    script_path = os.path.join(script_dir, script_name)
    
    if not os.path.exists(script_path):
        print(f"{COLOR_RED}❌ Error: Scraper script not found at '{script_path}'{COLOR_RESET}")
        return False, 0.0, "Script not found"

    print(f"{COLOR_CYAN}[EXEC] Launching {script_name} with parameters: {' '.join(args)}...{COLOR_RESET}\n")
    
    start_time = time.time()
    try:
        # Run subprocess and stream output live to terminal stdout
        process = subprocess.Popen(
            [sys.executable, script_path] + args,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            bufsize=1
        )
        
        last_status_line = ""
        for line in process.stdout:
            print(f"  {COLOR_GREEN}│{COLOR_RESET} {line}", end="")
            stripped = line.strip()
            if stripped:
                last_status_line = stripped
                
        process.wait()
        elapsed = time.time() - start_time
        
        if process.returncode == 0:
            print(f"\n{COLOR_GREEN}✔ Successfully executed {script_name} ({elapsed:.2f}s){COLOR_RESET}")
            return True, elapsed, last_status_line
        else:
            print(f"\n{COLOR_RED}❌ Error executing {script_name} (Exit Code: {process.returncode}){COLOR_RESET}")
            return False, elapsed, f"Failed with exit code {process.returncode}"
    except Exception as e:
        elapsed = time.time() - start_time
        print(f"\n{COLOR_RED}❌ Subprocess exception while running {script_name}: {e}{COLOR_RESET}")
        return False, elapsed, str(e)

def main():
    print(BANNER)
    
    parser = argparse.ArgumentParser(description="Master S.H.I.E.L.D. Scraper Suite Runner")
    group = parser.add_mutually_exclusive_group()
    group.add_argument("-t", "--test", action="store_true", help="Execute in TEST mode (Quick card test + items + operations)")
    group.add_argument("-s", "--sandbox", action="store_true", help="Execute in SANDBOX mode (Scrapes up to 3 cards per category)")
    group.add_argument("-f", "--full", action="store_true", help="Execute a FULL scan (Retrieves all entries; might take several minutes)")
    
    args = parser.parse_args()
    
    # Default to test/sandbox if no flags passed
    mode_name = "SANDBOX SCAN"
    card_args = ["--sandbox"]
    
    if args.test:
        mode_name = "TEST / INTEGRATION DRY-RUN"
        card_args = ["--test"]
    elif args.full:
        mode_name = "FULL DATA REFRESH HARVEST"
        card_args = []
        print(f"{COLOR_YELLOW}⚠️ WARNING: Full scan will crawl all card pages on Fandom Wiki with throttling delays.")
        print(f"This process will take a significant amount of time. Respecting Wiki server policies.{COLOR_RESET}\n")
        confirm = input(f"Are you sure you want to proceed with a FULL card scan? (y/N): ").strip().lower()
        if confirm != 'y':
            print(f"{COLOR_CYAN}Reverting to SANDBOX scan mode.{COLOR_RESET}")
            mode_name = "SANDBOX SCAN (REVERTED)"
            card_args = ["--sandbox"]

    print(f"{COLOR_CYAN}Executing Scrapers under Protocol: {COLOR_BOLD}{mode_name}{COLOR_RESET}")
    
    results = []
    
    # 1. Card Scraper
    print_section_header("SCRAPER 1 OF 3: HERO CARDS & BLUEPRINTS")
    success1, time1, details1 = run_scraper_subprocess("wiki_scraper.py", card_args)
    results.append({
        "name": "Hero Cards Scraper (wiki_scraper.py)",
        "success": success1,
        "time": time1,
        "details": details1
    })
    
    # 2. Items Scraper
    print_section_header("SCRAPER 2 OF 3: RESTORATIVES & ITEMS")
    success2, time2, details2 = run_scraper_subprocess("wiki_items_scraper.py")
    results.append({
        "name": "Restorative Items Scraper (wiki_items_scraper.py)",
        "success": success2,
        "time": time2,
        "details": details2
    })
    
    # 3. Operations Scraper
    print_section_header("SCRAPER 3 OF 3: S.H.I.E.L.D. TACTICAL OPERATIONS")
    success3, time3, details3 = run_scraper_subprocess("wiki_operations_scraper.py")
    results.append({
        "name": "Tactical Operations Scraper (wiki_operations_scraper.py)",
        "success": success3,
        "time": time3,
        "details": details3
    })
    
    # Render Master Statistics Table
    print("\n")
    print(f"{COLOR_CYAN}┌─────────────────────────────────────────────────────────────────────────────┐{COLOR_RESET}")
    print(f"{COLOR_CYAN}│                   {COLOR_BOLD}S.H.I.E.L.D. SCRAPER RUN COMPLETE SUMMARY{COLOR_RESET}{COLOR_CYAN}                 │{COLOR_RESET}")
    print(f"{COLOR_CYAN}├─────────────────────────────────────────────────────────────────────────────┤{COLOR_RESET}")
    
    total_time = 0.0
    all_ok = True
    
    for r in results:
        status_color = COLOR_GREEN if r["success"] else COLOR_RED
        status_lbl = "OK  " if r["success"] else "FAIL"
        total_time += r["time"]
        if not r["success"]:
            all_ok = False
            
        # Format script name padding
        name_pad = r["name"].ljust(50)
        time_pad = f"{r['time']:.2f}s".rjust(7)
        
        print(f"{COLOR_CYAN}│{COLOR_RESET} [{status_color}{status_lbl}{COLOR_RESET}] {name_pad} | {time_pad} {COLOR_CYAN}│{COLOR_RESET}")
        print(f"{COLOR_CYAN}│       ├─ Log: {COLOR_CYAN}{r['details'][:60]}{COLOR_RESET}".ljust(88) + f"{COLOR_CYAN}│{COLOR_RESET}")
        
    print(f"{COLOR_CYAN}├─────────────────────────────────────────────────────────────────────────────┤{COLOR_RESET}")
    overall_status_color = COLOR_GREEN if all_ok else COLOR_RED
    overall_status_lbl = "ALL SECURED" if all_ok else "ENGAGEMENT WARNING // FAILURE DETECTED"
    print(f"{COLOR_CYAN}│{COLOR_RESET} OVERALL STATUS: {overall_status_color}{overall_status_lbl.ljust(43)}{COLOR_RESET} | TOTAL TIME: {f'{total_time:.2f}s'.rjust(7)} {COLOR_CYAN}│{COLOR_RESET}")
    print(f"{COLOR_CYAN}└─────────────────────────────────────────────────────────────────────────────┘{COLOR_RESET}")

if __name__ == "__main__":
    main()
