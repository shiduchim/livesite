"""
Redraw the EK ribbon icons as clean vector shapes.

Geometry follows the original 96 px artwork, snapped to a 32-unit grid (3 px
in the original) so edges land on whole pixels when Revit shows the icon at
32 px (100 %) or 64 px (200 %). Rendered supersampled and saved at twice the ribbon size with 192 DPI
metadata (64 px for the 32 px large icon, 32 px for the 16 px small icon),
so WPF draws them sharp at 100 % and 200 % display scaling.
"""
import sys
from PIL import Image, ImageChops, ImageDraw

CANVAS = 1024
K = CANVAS / 96.0          # original 96 px design units -> canvas px

BLUE = (38, 104, 191, 255)
WHITE = (255, 255, 255, 255)


def P(*values):
    return [v * K for v in values]


def background(draw):
    draw.rounded_rectangle(P(6, 6, 90, 90), radius=15 * K, fill=BLUE)


def letters(img, stroke):
    """'EK' built from strokes: 7 units tall (21 px of 96), grid aligned."""
    draw = ImageDraw.Draw(img)
    top, bottom = 60, 81
    mid = (top + bottom) / 2.0
    s = stroke

    # E
    ex0, ex1 = 33, 45
    draw.rectangle(P(ex0, top, ex0 + s, bottom), fill=WHITE)
    draw.rectangle(P(ex0, top, ex1, top + s), fill=WHITE)
    draw.rectangle(P(ex0, mid - s / 2.0, ex1 - 1.5, mid + s / 2.0), fill=WHITE)
    draw.rectangle(P(ex0, bottom - s, ex1, bottom), fill=WHITE)

    # K
    kx0, kx1 = 48, 63
    draw.rectangle(P(kx0, top, kx0 + s, bottom), fill=WHITE)
    arms = Image.new("L", img.size, 0)
    arm_draw = ImageDraw.Draw(arms)
    width = int(s * 1.12 * K)
    join_x = kx0 + s
    arm_draw.line(P(join_x - 1, mid + 1.5, kx1 + 2, top - 3), fill=255, width=width)
    arm_draw.line(P(join_x + 3.2, mid - 1.6, kx1 + 2, bottom + 3), fill=255, width=width)
    clip = Image.new("L", img.size, 0)
    ImageDraw.Draw(clip).rectangle(P(kx0, top, kx1, bottom), fill=255)
    arms = ImageChops.multiply(arms, clip)
    img.paste(WHITE, (0, 0), arms)


def folder_and_play(draw):
    # Folder outline with tab (left), play triangle (right).
    draw.polygon(P(15, 18, 24, 18, 30, 24, 48, 24, 48, 48, 15, 48), fill=WHITE)
    draw.rounded_rectangle(P(21, 30, 42, 42), radius=3 * K, fill=BLUE)
    draw.polygon(P(60, 18, 78, 33, 60, 48), fill=WHITE)


def big_play(draw):
    draw.polygon(P(30, 17, 67, 36, 30, 55), fill=WHITE)


def render(kind, stroke, size):
    img = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    background(draw)
    if kind == "launcher":
        folder_and_play(draw)
    else:
        big_play(draw)
    letters(img, stroke)
    return img.resize((size, size), Image.BOX)


if __name__ == "__main__":
    out_dir = sys.argv[1]
    for kind in ("launcher", "quick"):
        for dip in (32, 16):
            render(kind, 3.0, dip * 2).save(
                "{0}/{1}_{2}.png".format(out_dir, kind, dip), dpi=(192, 192), optimize=True)
