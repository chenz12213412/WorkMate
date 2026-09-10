from pathlib import Path
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
ICONS_DIR = ROOT / "Assets" / "Icons"
ICON_PACK_DIR = ICONS_DIR / "WorkMate_UI_IconPack"
APP_ICON_SOURCE = ICON_PACK_DIR / "App" / "workmate_default.png"
ICON_PATH = ICONS_DIR / "WorkMate.ico"
GENERATED_DIR = ICONS_DIR / "Generated"

ICO_SIZES = (16, 20, 24, 32, 48, 64, 128, 256)
UI_ICON_GROUPS = ("Metrics", "Actions", "Navigation")
UI_ICON_CANVAS_SIZE = 256
UI_ICON_PADDING_RATIO = 0.08
ALPHA_THRESHOLD = 8


def normalize_ui_icon(source_path: Path, destination_path: Path) -> tuple[tuple[int, int, int, int], tuple[int, int, int, int]]:
    with Image.open(source_path) as source:
        rgba = source.convert("RGBA")
        alpha = rgba.getchannel("A")
        visible_alpha = alpha.point(lambda value: 255 if value > ALPHA_THRESHOLD else 0)
        source_bbox = visible_alpha.getbbox()
        if source_bbox is None:
            raise ValueError(f"Icon has no visible pixels: {source_path}")

        cropped = rgba.crop(source_bbox)
        available_size = round(UI_ICON_CANVAS_SIZE * (1 - 2 * UI_ICON_PADDING_RATIO))
        scale = min(available_size / cropped.width, available_size / cropped.height)
        resized_size = (
            max(1, round(cropped.width * scale)),
            max(1, round(cropped.height * scale)),
        )
        resized = cropped.resize(resized_size, Image.Resampling.LANCZOS)
        canvas = Image.new("RGBA", (UI_ICON_CANVAS_SIZE, UI_ICON_CANVAS_SIZE), (0, 0, 0, 0))
        offset = (
            (UI_ICON_CANVAS_SIZE - resized.width) // 2,
            (UI_ICON_CANVAS_SIZE - resized.height) // 2,
        )
        canvas.alpha_composite(resized, offset)

        destination_path.parent.mkdir(parents=True, exist_ok=True)
        canvas.save(destination_path, format="PNG", optimize=True)

        generated_bbox = (
            offset[0],
            offset[1],
            offset[0] + resized.width,
            offset[1] + resized.height,
        )
        return source_bbox, generated_bbox


def prepare() -> None:
    if not APP_ICON_SOURCE.exists():
        raise FileNotFoundError(f"Missing official app icon source: {APP_ICON_SOURCE}")

    with Image.open(APP_ICON_SOURCE) as image:
        image.convert("RGBA").save(
            ICON_PATH,
            format="ICO",
            sizes=[(size, size) for size in ICO_SIZES],
            bitmap_format="png",
        )

    print("Normalized UI icons:")
    for group in UI_ICON_GROUPS:
        source_dir = ICON_PACK_DIR / group
        destination_dir = GENERATED_DIR / group
        for source_path in sorted(source_dir.glob("*.png")):
            destination_path = destination_dir / source_path.name
            source_bbox, generated_bbox = normalize_ui_icon(source_path, destination_path)
            source_coverage = max(
                (source_bbox[2] - source_bbox[0]) / UI_ICON_CANVAS_SIZE,
                (source_bbox[3] - source_bbox[1]) / UI_ICON_CANVAS_SIZE,
            )
            generated_coverage = max(
                (generated_bbox[2] - generated_bbox[0]) / UI_ICON_CANVAS_SIZE,
                (generated_bbox[3] - generated_bbox[1]) / UI_ICON_CANVAS_SIZE,
            )
            print(
                f"  {group}/{source_path.name}: "
                f"{source_coverage:.1%} -> {generated_coverage:.1%}"
            )

    print(f"Generated application icon from: {APP_ICON_SOURCE}")
    print(f"Generated multi-size application icon: {ICON_PATH}")


if __name__ == "__main__":
    prepare()
