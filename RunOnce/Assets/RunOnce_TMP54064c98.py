"""
此脚本需要在"确保兼容"模式才能运行
"""

from PIL import Image
import os


def generate_asset(source_img, target_width, target_height, output_path, padding_ratio=0.0):
    """
    将源图等比缩放到目标尺寸内（不压缩变形），居中放置在透明画布上。
    padding_ratio: 内边距比例，0.0 表示铺满，0.1 表示四周留 10% 空白。
    """
    canvas = Image.new("RGBA", (target_width, target_height), (0, 0, 0, 0))

    available_w = max(1, int(target_width * (1 - 2 * padding_ratio)))
    available_h = max(1, int(target_height * (1 - 2 * padding_ratio)))

    src_w, src_h = source_img.size
    scale = min(available_w / src_w, available_h / src_h)

    new_w = max(1, int(src_w * scale))
    new_h = max(1, int(src_h * scale))

    resized = source_img.resize((new_w, new_h), Image.LANCZOS)

    x = (target_width - new_w) // 2
    y = (target_height - new_h) // 2

    canvas.paste(resized, (x, y), resized)
    canvas.save(output_path, "PNG")
    print(f"  ✔ {os.path.basename(output_path):55s}  {target_width}×{target_height}")


def main():
    assets_dir = os.path.dirname(os.path.abspath(__file__))
    source_path = os.path.join(assets_dir, "logo.png")

    if not os.path.exists(source_path):
        print(f"❌ 找不到源文件: {source_path}")
        return

    source = Image.open(source_path).convert("RGBA")
    print(f"源图: {source_path}  ({source.size[0]}×{source.size[1]})\n")

    # (文件名, 宽, 高)
    assets = [
        # LockScreenLogo  (基准 24×24)
        ("LockScreenLogo.scale-100.png",  24,   24),
        ("LockScreenLogo.scale-200.png",  48,   48),

        # SplashScreen  (基准 620×300)
        ("SplashScreen.scale-100.png",   620,  300),
        ("SplashScreen.scale-150.png",   930,  450),
        ("SplashScreen.scale-200.png",  1240,  600),
        ("SplashScreen.scale-400.png",  2480, 1200),

        # Square150x150Logo  (基准 150×150)
        ("Square150x150Logo.scale-100.png", 150, 150),
        ("Square150x150Logo.scale-150.png", 225, 225),
        ("Square150x150Logo.scale-200.png", 300, 300),
        ("Square150x150Logo.scale-400.png", 600, 600),

        # Square44x44Logo  (基准 44×44)
        ("Square44x44Logo.scale-100.png",  44,  44),
        ("Square44x44Logo.scale-150.png",  66,  66),
        ("Square44x44Logo.scale-200.png",  88,  88),
        ("Square44x44Logo.scale-400.png", 176, 176),

        # Square44x44Logo targetsize altform-unplated
        ("Square44x44Logo.targetsize-16_altform-unplated.png",   16,  16),
        ("Square44x44Logo.targetsize-24_altform-unplated.png",   24,  24),
        ("Square44x44Logo.targetsize-32_altform-unplated.png",   32,  32),
        ("Square44x44Logo.targetsize-48_altform-unplated.png",   48,  48),
        ("Square44x44Logo.targetsize-256_altform-unplated.png", 256, 256),

        # StoreLogo  (基准 50×50)
        ("StoreLogo.png", 50, 50),

        # Wide310x150Logo  (基准 310×150)
        ("Wide310x150Logo.scale-100.png",  310,  150),
        ("Wide310x150Logo.scale-150.png",  465,  225),
        ("Wide310x150Logo.scale-200.png",  620,  300),
        ("Wide310x150Logo.scale-400.png", 1240,  600),
    ]

    for filename, w, h in assets:
        output_path = os.path.join(assets_dir, filename)
        generate_asset(source, w, h, output_path)

    print(f"\n✅ 完成，共生成 {len(assets)} 张资源图。")


if __name__ == "__main__":
    main()