"""Writes the final icons: three rungs climbing to a bright top step."""
import math
import pathlib
import sys

from PIL import Image, ImageDraw

project = pathlib.Path(sys.argv[1])
SS = 2048
INK = (9, 10, 13, 255)
WHITE = (242, 240, 235, 255)
VOLT = (230, 255, 87, 255)
SLANT = math.tan(math.radians(14))
K = SS / 1024

image = Image.new("RGBA", (SS, SS), (0, 0, 0, 0))
d = ImageDraw.Draw(image)
d.rounded_rectangle((0, 0, SS - 1, SS - 1), radius=int(190 * K), fill=INK)


def box(x0, y0, x1, y1):
    def shear(x, y):
        return ((x + (512 - y) * SLANT) * K, y * K)
    return [shear(x0, y0), shear(x1, y0), shear(x1, y1), shear(x0, y1)]


for i, y in enumerate((644, 430, 216)):
    x = 150 + i * 92
    d.polygon(box(x, y, x + 540, y + 164), fill=VOLT if i == 2 else WHITE)

image.resize((128, 128), Image.LANCZOS).save(project / "icon.png", optimize=True)
image.resize((256, 256), Image.LANCZOS).save(
    project / "launcher" / "icon.ico",
    format="ICO",
    sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    bitmap_format="bmp",
)
print((project / "icon.png").stat().st_size, (project / "launcher" / "icon.ico").stat().st_size)
