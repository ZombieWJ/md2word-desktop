"""Export the repository's simple SVG geometry to a multi-size Windows icon.

Requires Pillow only; application builds use the committed ICO directly.
"""
from pathlib import Path
import xml.etree.ElementTree as ET
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "resources/branding"
SCALE = 4
canvas = Image.new("RGBA", (256 * SCALE, 256 * SCALE), (0, 0, 0, 0))
draw = ImageDraw.Draw(canvas)
for element in ET.parse(ASSETS / "app.svg").getroot():
    tag = element.tag.rsplit("}", 1)[-1]
    if tag == "rect":
        x, y, width, height = (float(element.attrib[key]) * SCALE for key in ("x", "y", "width", "height"))
        draw.rounded_rectangle((x, y, x + width, y + height), radius=float(element.get("rx", "0")) * SCALE, fill=element.attrib["fill"])
    elif tag == "polygon":
        points = [tuple(float(value) * SCALE for value in pair.split(",")) for pair in element.attrib["points"].split()]
        draw.polygon(points, fill=element.attrib["fill"])
    else:
        raise ValueError(f"Unsupported SVG element: {tag}")
image = canvas.resize((256, 256), Image.Resampling.LANCZOS)
image.save(ASSETS / "app.ico", sizes=[(size, size) for size in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
preview = Image.new("RGB", (512, 300), "#f4f6fa")
preview.paste(image, (28, 22), image)
for size, x in [(16, 310), (24, 354), (32, 406), (48, 310), (64, 390)]:
    tile = image.resize((size, size), Image.Resampling.LANCZOS)
    preview.paste(tile, (x, 62 if size <= 32 else 140), tile)
output = ROOT / "artifacts/icon-fix"
output.mkdir(parents=True, exist_ok=True)
preview.save(output / "icon-preview.png")
print("Generated resources/branding/app.ico (16–256px).")
