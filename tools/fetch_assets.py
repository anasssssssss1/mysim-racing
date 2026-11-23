"""Download the CC0 (public domain) textures and sky photos the game uses, from Poly Haven.

usage: python tools/fetch_assets.py [dest=assets/cc0]
Files are not committed; CI runs this before importing (and caches the result).
"""
import json, os, sys, urllib.request

TEXTURES = ["asphalt_track", "asphalt_pit_lane", "coral_gravel", "grass_ground", "aerial_sand",
            "painted_concrete", "concrete_floor_worn_001", "brushed_concrete"]
MAPS = {"Diffuse": "diff", "nor_gl": "nor_gl", "Rough": "rough"}
SKIES = ["kloofendal_48d_partly_cloudy_puresky"]
UA = {"User-Agent": "mysim-racing-asset-fetch/1.0"}


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=120) as r:
        return r.read()


def save(url, path):
    if os.path.exists(path):
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    data = get(url)
    with open(path + ".part", "wb") as f:
        f.write(data)
    os.replace(path + ".part", path)
    print(f"{path} {len(data) // 1024} KB")


def main():
    dest = sys.argv[1] if len(sys.argv) > 1 else "assets/cc0"
    for t in TEXTURES:
        files = json.loads(get(f"https://api.polyhaven.com/files/{t}"))
        for key, short in MAPS.items():
            save(files[key]["2k"]["jpg"]["url"], f"{dest}/{t}/{t}_{short}_2k.jpg")
    for s in SKIES:
        files = json.loads(get(f"https://api.polyhaven.com/files/{s}"))
        save(files["hdri"]["4k"]["hdr"]["url"], f"{dest}/sky/{s}_4k.hdr")
    with open(f"{dest}/CREDITS.txt", "w") as f:
        f.write("Textures and HDRIs from Poly Haven (https://polyhaven.com), CC0 public domain.\n"
                + "\n".join(TEXTURES + SKIES) + "\n")


if __name__ == "__main__":
    main()
