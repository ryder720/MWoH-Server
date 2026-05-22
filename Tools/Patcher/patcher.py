#!/usr/bin/env python3
"""
====================================================================
Marvel: War of Heroes (MWoH) — Automated APK Patcher
====================================================================
This script automates the decompilation, search-and-replace redirection
of endpoints, HTTPS-to-HTTP protocol downgrades, and debug-signing 
of any base MWoH APK to target your custom private server IP.

Requires:
  - Python 3.x
  - Java Runtime Environment (JRE) in system PATH (for apktool/signer)
"""

import os
import re
import sys
import shutil
import argparse
import subprocess
import urllib.request

# Tool URLs
APKTOOL_URL = "https://github.com/iBotPeaches/Apktool/releases/download/v2.9.3/apktool_2.9.3.jar"
UBER_SIGNER_URL = "https://github.com/patrickfav/uber-apk-signer/releases/download/v1.3.0/uber-apk-signer-1.3.0.jar"

# Paths
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
BIN_DIR = os.path.join(SCRIPT_DIR, "bin")
APKTOOL_JAR = os.path.join(BIN_DIR, "apktool.jar")
UBER_SIGNER_JAR = os.path.join(BIN_DIR, "uber-apk-signer.jar")
TEMP_DIR = os.path.join(SCRIPT_DIR, "temp_decompiled")
TEMP_UNSIGNED_APK = os.path.join(SCRIPT_DIR, "patched_unsigned.apk")


def check_java():
    """Verifies that Java is installed and accessible in the system PATH."""
    print("[*] Verifying Java Runtime Environment (JRE) installation...")
    try:
        # Run java -version without throwing console popups
        result = subprocess.run(
            ["java", "-version"],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            check=True
        )
        # Java outputs version to stderr
        version_line = result.stderr.splitlines()[0] if result.stderr else "Unknown version"
        print(f"[+] Java detected successfully: {version_line}")
        return True
    except (subprocess.CalledProcessError, FileNotFoundError):
        print("[-] ERROR: Java Runtime Environment (JRE) is missing or not in system PATH!")
        print("    Java is required to execute 'apktool' and 'uber-apk-signer'.")
        print("    Please download and install Java (JRE/JDK) and try again.")
        return False


def download_progress(block_num, block_size, total_size):
    """Callback to print a premium progress percentage for downloads."""
    downloaded = block_num * block_size
    percent = min(100.0, (downloaded / total_size) * 100.0) if total_size > 0 else 0.0
    sys.stdout.write(f"\r    Downloading: {percent:.2f}% ({downloaded}/{total_size} bytes)")
    sys.stdout.flush()


def ensure_tools():
    """Checks for required jar files, downloading them if missing."""
    os.makedirs(BIN_DIR, exist_ok=True)

    if not os.path.exists(APKTOOL_JAR):
        print(f"[*] 'apktool.jar' is missing. Downloading from stable GitHub releases...")
        print(f"    URL: {APKTOOL_URL}")
        try:
            urllib.request.urlretrieve(APKTOOL_URL, APKTOOL_JAR, download_progress)
            print("\n[+] Download completed successfully!")
        except Exception as e:
            print(f"\n[-] ERROR: Failed to download apktool: {e}")
            return False

    if not os.path.exists(UBER_SIGNER_JAR):
        print(f"[*] 'uber-apk-signer.jar' is missing. Downloading from stable GitHub releases...")
        print(f"    URL: {UBER_SIGNER_URL}")
        try:
            urllib.request.urlretrieve(UBER_SIGNER_URL, UBER_SIGNER_JAR, download_progress)
            print("\n[+] Download completed successfully!")
        except Exception as e:
            print(f"\n[-] ERROR: Failed to download uber-apk-signer: {e}")
            return False

    return True


def decompile_apk(apk_path):
    """Decompiles the target APK using apktool."""
    print(f"[*] Decompiling game client APK: {os.path.basename(apk_path)}...")
    if os.path.exists(TEMP_DIR):
        print("    Cleaning existing temporary decompile folder...")
        shutil.rmtree(TEMP_DIR)

    cmd = [
        "java", "-jar", APKTOOL_JAR,
        "d", apk_path,
        "-o", TEMP_DIR,
        "-f"
    ]
    try:
        subprocess.run(cmd, check=True)
        print("[+] APK decompiled successfully!")
        return True
    except subprocess.CalledProcessError as e:
        print(f"[-] ERROR: Decompilation failed: {e}")
        return False


def patch_manifest():
    """Injects android:usesCleartextTraffic='true' into AndroidManifest.xml."""
    manifest_path = os.path.join(TEMP_DIR, "AndroidManifest.xml")
    if not os.path.exists(manifest_path):
        print("[-] ERROR: AndroidManifest.xml not found!")
        return False

    print("[*] Patching AndroidManifest.xml to permit cleartext plain-HTTP traffic...")
    with open(manifest_path, "r", encoding="utf-8") as f:
        content = f.read()

    # If it's already there, skip
    if "android:usesCleartextTraffic" in content:
        print("    usesCleartextTraffic is already configured. Skipping.")
        return True

    # Find the <application tag and inject attribute
    match = re.search(r"<application([^>]*)>", content)
    if match:
        original_attrs = match.group(1)
        patched_tag = f'<application{original_attrs} android:usesCleartextTraffic="true">'
        patched_content = content.replace(match.group(0), patched_tag)
        
        with open(manifest_path, "w", encoding="utf-8") as f:
            f.write(patched_content)
        print("[+] Manifest patched successfully!")
        return True
    else:
        print("[-] ERROR: Could not locate <application> tag inside manifest!")
        return False


def patch_game_server(ip, port):
    """Patches res/values/strings.xml to point gameServer to the custom server."""
    strings_path = os.path.join(TEMP_DIR, "res", "values", "strings.xml")
    if not os.path.exists(strings_path):
        print("[-] ERROR: res/values/strings.xml not found!")
        return False

    ip_port = f"{ip}:{port}"
    print(f"[*] Redirecting Cygames gameServer URL to: {ip_port}...")
    with open(strings_path, "r", encoding="utf-8") as f:
        content = f.read()

    # Search for gameServer string entry
    pattern = re.compile(r'<string name="gameServer">([^<]+)</string>')
    match = pattern.search(content)
    if match:
        current_val = match.group(1)
        print(f"    Currently set to: {current_val}")
        # Build replacement
        replacement = f'<string name="gameServer">{ip_port}/ultimate/</string>'
        patched_content = pattern.sub(replacement, content)
        
        with open(strings_path, "w", encoding="utf-8") as f:
            f.write(patched_content)
        print("[+] gameServer redirected successfully!")
        return True
    else:
        print("[-] ERROR: Could not locate 'gameServer' key inside strings.xml!")
        return False


def patch_mobage_endpoints(ip, port):
    """Patches ServerMode.smali endpoints to point to custom private server IP:PORT."""
    server_mode_path = os.path.join(TEMP_DIR, "smali", "com", "mobage", "global", "android", "ServerMode.smali")
    if not os.path.exists(server_mode_path):
        print("[-] ERROR: ServerMode.smali not found!")
        return False

    ip_port = f"{ip}:{port}"
    print(f"[*] Redirecting Mobage social/bank/CDN endpoint hostnames to: {ip_port}...")
    with open(server_mode_path, "r", encoding="utf-8") as f:
        content = f.read()

    # Locate and replace any string literals that contain typical Mobage domains or our local emulator IP
    domain_pattern = re.compile(r'(const-string\s+v\d+,\s*)"([^"]*(?:mbga\.jp|mobage\.com|dena\.jp|10\.0\.2\.2:\d+|localhost:\d+)[^"]*)"')
    
    modified_count = 0
    lines = content.splitlines()
    for idx, line in enumerate(lines):
        match = domain_pattern.search(line)
        if match:
            reg_instr = match.group(1)
            original_str = match.group(2)
            # Replace host/domain in the string with target IP:PORT
            lines[idx] = f'{reg_instr}"{ip_port}"'
            modified_count += 1

    if modified_count > 0:
        patched_content = "\n".join(lines)
        with open(server_mode_path, "w", encoding="utf-8") as f:
            f.write(patched_content)
        print(f"[+] ServerMode.smali patched successfully! (Modified {modified_count} strings)")
        return True
    else:
        print("[-] WARNING: No Mobage domain string matches found inside ServerMode.smali!")
        return False


def patch_analytics_telemetry(ip, port):
    """Redirects ngpipes analytic endpoints and downgrades HTTPS connections inside EventReporterSessionFactory.smali."""
    factory_path = os.path.join(TEMP_DIR, "smali", "com", "mobage", "android", "analytics", "internal", "EventReporterSessionFactory.smali")
    if not os.path.exists(factory_path):
        print("[-] ERROR: EventReporterSessionFactory.smali not found!")
        return False

    # Convert port int to lower hex format (e.g. 5000 -> 0x1388)
    hex_port = f"0x{port:x}"
    print(f"[*] Redirecting Mobage analytics telemetry host to '{ip}' and port to '{port}' ({hex_port})...")
    with open(factory_path, "r", encoding="utf-8") as f:
        content = f.read()

    # 1. Patch analytics host string
    host_pattern = re.compile(r'(const-string\s+v\d+,\s*)"(?:ngpipes\.mobage\.com|ngpipes-sandbox\.mobage\.com|10\.0\.2\.2)"')
    content, count_host = host_pattern.subn(rf'\g<1>"{ip}"', content)

    # 2. Patch connection port hex value (always in register v2 before HttpHost init)
    port_pattern = re.compile(r'(const/16\s+v2,\s*)(?:0x1bb|0x1388|0x[0-9a-fA-F]+)')
    content, count_port = port_pattern.subn(rf'\g<1>{hex_port}', content)

    # 3. Downgrade telemetry connection protocol scheme from https to http
    scheme_pattern = re.compile(r'(const-string\s+v\d+,\s*)"(?:https|http)"')
    content, count_scheme = scheme_pattern.subn(rf'\g<1>"http"', content)

    with open(factory_path, "w", encoding="utf-8") as f:
        f.write(content)

    print(f"[+] Telemetry redirected: {count_host} hosts, {count_port} ports, {count_scheme} protocol schemes patched.")
    return True


def global_https_downgrade():
    """Traverses all smali files and converts all dynamic HTTPS scheme constants into HTTP."""
    print("[*] Running global bytecode HTTPS-to-HTTP protocol downgrade across all Smali classes...")
    
    smali_dir = os.path.join(TEMP_DIR, "smali")
    if not os.path.exists(smali_dir):
        print("[-] ERROR: smali folder not found!")
        return False

    pattern = re.compile(r'(const-string\s+v\d+,\s*)"https://"')
    modified_files = 0

    for root, dirs, files in os.walk(smali_dir):
        for file in files:
            if file.endswith('.smali'):
                filepath = os.path.join(root, file)
                try:
                    with open(filepath, 'r', encoding='utf-8', errors='ignore') as f:
                        content = f.read()
                    
                    if pattern.search(content):
                        # Replace https:// with http://
                        new_content = pattern.sub(r'\g<1>"http://"', content)
                        with open(filepath, 'w', encoding='utf-8') as f:
                            f.write(new_content)
                        modified_files += 1
                except Exception as e:
                    print(f"    [-] Error processing file {file}: {e}")

    print(f"[+] Downgraded dynamic HTTPS connections in {modified_files} smali classes!")
    return True


def compile_apk(output_apk_path):
    """Compiles the patched decompiled directory back into an unsigned APK."""
    print("[*] Recompiling modified bytecode and resources into APK...")
    cmd = [
        "java", "-jar", APKTOOL_JAR,
        "b", TEMP_DIR,
        "-o", TEMP_UNSIGNED_APK
    ]
    try:
        subprocess.run(cmd, check=True)
        print("[+] Patch built into unsigned package successfully!")
        return True
    except subprocess.CalledProcessError as e:
        print(f"[-] ERROR: Recompilation failed: {e}")
        return False


def sign_apk(output_path):
    """Signs and aligns the APK using uber-apk-signer, then moves it to output_path."""
    print("[*] Aligning and signing patched APK using debug-keystore...")
    
    # Run uber-apk-signer
    cmd = [
        "java", "-jar", UBER_SIGNER_JAR,
        "--apks", TEMP_UNSIGNED_APK,
        "--out", SCRIPT_DIR
    ]
    try:
        subprocess.run(cmd, check=True)
        
        # uber-apk-signer names signed files as [name]-aligned-debugSigned.apk
        expected_output = os.path.join(SCRIPT_DIR, "patched_unsigned-aligned-debugSigned.apk")
        if os.path.exists(expected_output):
            # Clean up destination if it exists
            if os.path.exists(output_path):
                os.remove(output_path)
                
            shutil.move(expected_output, output_path)
            print(f"[+] Premium patch process COMPLETE!")
            print(f"[+] Signed game client output: {output_path}")
            return True
        else:
            print("[-] ERROR: Signed APK file was not generated at expected location!")
            return False
    except subprocess.CalledProcessError as e:
        print(f"[-] ERROR: Signing process failed: {e}")
        return False


def cleanup():
    """Cleans up temporary decompiled folders and files."""
    print("[*] Cleaning up temporary workspace files...")
    if os.path.exists(TEMP_DIR):
        shutil.rmtree(TEMP_DIR)
    if os.path.exists(TEMP_UNSIGNED_APK):
        os.remove(TEMP_UNSIGNED_APK)
    print("[+] Workspace tidy completed.")


def main():
    parser = argparse.ArgumentParser(
        description="Marvel: War of Heroes — Client APK Patcher Tool",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""\
Example:
  python patcher.py --ip 10.0.2.2 --port 5000 base_game.apk
  python patcher.py --ip 192.168.1.100 base_game.apk
"""
    )
    parser.add_argument("apk_path", help="Path to the untouched base game APK")
    parser.add_argument("--ip", default="10.0.2.2", help="Private Server IP address (default: 10.0.2.2 for Android Emulator host loopback)")
    parser.add_argument("--port", default="5000", type=int, help="Private Server Port (default: 5000)")
    parser.add_argument("--output", default="marvel_woh_patched.apk", help="Output path for the final patched, signed APK")
    parser.add_argument("--skip-java", action="store_true", help="Skip local Java Runtime installation verification")
    parser.add_argument("--keep-temp", action="store_true", help="Keep intermediate decompiled directories for debugging")

    args = parser.parse_args()

    print("====================================================================")
    print("      MARVEL: WAR OF HEROES — AUTOMATED CLIENT APK PATCHER")
    print("====================================================================")
    print(f"Target Server IP : {args.ip}")
    print(f"Target Server Port: {args.port}")
    print(f"Input Base APK   : {args.apk_path}")
    print(f"Output Patched APK: {args.output}")
    print("--------------------------------------------------------------------")

    # 1. Input Validation
    if not os.path.exists(args.apk_path):
        print(f"[-] ERROR: Target APK '{args.apk_path}' does not exist!")
        sys.exit(1)

    # 2. Check Java
    if not args.skip_java and not check_java():
        sys.exit(1)

    # 3. Ensure required binaries are ready
    if not ensure_tools():
        sys.exit(1)

    # 4. Perform patching cycle
    success = False
    try:
        if decompile_apk(args.apk_path):
            if (patch_manifest() and 
                patch_game_server(args.ip, args.port) and 
                patch_mobage_endpoints(args.ip, args.port) and 
                patch_analytics_telemetry(args.ip, args.port) and 
                global_https_downgrade()):
                
                if compile_apk(args.output):
                    if sign_apk(args.output):
                        success = True
    except Exception as ex:
        print(f"\n[-] CRITICAL EXCEPTION during patch operation: {ex}")
    finally:
        if not args.keep_temp:
            cleanup()

    if success:
        print("\n====================================================================")
        print(" [SUCCESS] Your patched APK is ready for deployment!")
        print(" Install it onto your emulator (e.g. Bluestacks) to play!")
        print("====================================================================")
    else:
        print("\n====================================================================")
        print(" [FAILURE] Patching process encountered errors.")
        print(" Please verify the logs above and try again.")
        print("====================================================================")
        sys.exit(1)


if __name__ == "__main__":
    main()
