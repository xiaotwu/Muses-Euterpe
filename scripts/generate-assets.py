#!/usr/bin/env python3
import os
from PIL import Image

def generate_assets():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    repo_root = os.path.dirname(script_dir)
    src_icon = os.path.join(repo_root, "resources", "images", "icon.png")
    out_dir = os.path.join(repo_root, "src", "Muses.App", "Assets", "Packaging")
    os.makedirs(out_dir, exist_ok=True)

    img = Image.open(src_icon).convert("RGBA")

    # 1. Square logos
    sizes = {
        "Square44x44Logo.png": (44, 44),
        "Square71x71Logo.png": (71, 71),
        "Square150x150Logo.png": (150, 150),
        "Square310x310Logo.png": (310, 310),
        "StoreLogo.png": (50, 50),
        "Square44x44Logo.targetsize-16.png": (16, 16),
        "Square44x44Logo.targetsize-24.png": (24, 24),
        "Square44x44Logo.targetsize-32.png": (32, 32),
        "Square44x44Logo.targetsize-48.png": (48, 48),
        "Square44x44Logo.targetsize-256.png": (256, 256),
    }

    for name, (w, h) in sizes.items():
        resized = img.resize((w, h), Image.Resampling.LANCZOS)
        resized.save(os.path.join(out_dir, name), "PNG")

    # 2. Wide and Splash with #1F1F1F background
    bg_color = (31, 31, 31, 255) # #1F1F1F

    # Wide310x150Logo
    wide = Image.new("RGBA", (310, 150), bg_color)
    icon_wide = img.resize((100, 100), Image.Resampling.LANCZOS)
    wide.paste(icon_wide, ((310 - 100) // 2, (150 - 100) // 2), icon_wide)
    wide.save(os.path.join(out_dir, "Wide310x150Logo.png"), "PNG")

    # SplashScreen 620x300
    splash = Image.new("RGBA", (620, 300), bg_color)
    icon_splash = img.resize((160, 160), Image.Resampling.LANCZOS)
    splash.paste(icon_splash, ((620 - 160) // 2, (300 - 160) // 2), icon_splash)
    splash.save(os.path.join(out_dir, "SplashScreen.png"), "PNG")

    # 3. Multi-resolution .ico for Windows app executable & tray
    ico_path = os.path.join(repo_root, "src", "Muses.App", "Assets", "icon.ico")
    ico_sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    img.save(ico_path, format="ICO", sizes=ico_sizes)

    # Copy icon.ico to resources/images/icon.ico as well
    res_ico_path = os.path.join(repo_root, "resources", "images", "icon.ico")
    img.save(res_ico_path, format="ICO", sizes=ico_sizes)

    print("All Windows and MSIX visual assets generated successfully.")

if __name__ == "__main__":
    generate_assets()
