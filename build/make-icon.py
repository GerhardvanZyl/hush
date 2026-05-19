"""
Generates the Hush pixel-art icon.

Concept: a "shushing face" — universal hush gesture. Designed at 32x32,
scaled with nearest-neighbor to keep crisp pixel edges at marketplace sizes.

Outputs:
  src/Hush.VS/Resources/hush-icon.png        (128x128, VSIX manifest Icon)
  src/Hush.VS/Resources/hush-preview.png     (200x200, VSIX PreviewImage)
  src/Hush.VSCode/icon.png                   (128x128, VSCode marketplace)
"""
import os
from PIL import Image

# Palette — soft, calm, "muted" feel
BG       = (42,  51,  64,  255)   # deep slate
FACE     = (245, 224, 188, 255)   # warm cream
FACE_SH  = (212, 187, 148, 255)   # face shadow
DARK     = (32,  38,  48,  255)   # outline / features
MOUTH    = (90,  60,  72,  255)   # muted plum (closed lips)
NAIL     = (236, 175, 152, 255)   # finger nail highlight

W = 32
H = 32

# Start with background
px = [[BG for _ in range(W)] for _ in range(H)]

def set_px(x, y, c):
    if 0 <= x < W and 0 <= y < H:
        px[y][x] = c

def rect(x0, y0, x1, y1, c):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            set_px(x, y, c)

# --- Face: a chunky pixel-art circle, rows 4..27, cols 6..25
face_rows = {
    4:  (11, 20),
    5:  (9,  22),
    6:  (8,  23),
    7:  (7,  24),
    8:  (7,  24),
    9:  (6,  25),
    10: (6,  25),
    11: (6,  25),
    12: (6,  25),
    13: (6,  25),
    14: (6,  25),
    15: (6,  25),
    16: (6,  25),
    17: (6,  25),
    18: (6,  25),
    19: (6,  25),
    20: (7,  24),
    21: (7,  24),
    22: (8,  23),
    23: (9,  22),
    24: (11, 20),
}
for y, (x0, x1) in face_rows.items():
    for x in range(x0, x1 + 1):
        set_px(x, y, FACE)

# Subtle face shadow on the bottom-right curve
shadow_pixels = [
    (24, 9), (25, 10), (25, 11), (25, 12), (25, 13),
    (25, 14), (25, 15), (25, 16), (25, 17), (25, 18),
    (25, 19), (24, 20), (24, 21), (23, 22), (22, 23),
    (20, 24),
]
for (x, y) in shadow_pixels:
    set_px(x, y, FACE_SH)

# Face outline — trace the boundary one pixel outside the fill on each row
def outline_face():
    for y, (x0, x1) in face_rows.items():
        set_px(x0 - 1, y, DARK)
        set_px(x1 + 1, y, DARK)
    # top + bottom caps
    set_px(12, 3, DARK); set_px(13, 3, DARK); set_px(14, 3, DARK)
    set_px(15, 3, DARK); set_px(16, 3, DARK); set_px(17, 3, DARK)
    set_px(18, 3, DARK); set_px(19, 3, DARK)
    set_px(12, 25, DARK); set_px(13, 25, DARK); set_px(14, 25, DARK)
    set_px(15, 25, DARK); set_px(16, 25, DARK); set_px(17, 25, DARK)
    set_px(18, 25, DARK); set_px(19, 25, DARK)
outline_face()

# --- Eyes (closed, content) — two short horizontal lines
# Left eye: cols 10..12, row 12
for x in range(10, 13):
    set_px(x, 12, DARK)
# Right eye: cols 19..21, row 12
for x in range(19, 22):
    set_px(x, 12, DARK)

# Tiny eyelash hint
set_px(12, 11, DARK)
set_px(19, 11, DARK)

# --- Mouth: a closed, slightly down-curved line
mouth_pixels = [(13, 18), (14, 17), (15, 17), (16, 17), (17, 17), (18, 18)]
for (x, y) in mouth_pixels:
    set_px(x, y, MOUTH)

# --- Shushing finger: vertical bar across the mouth
# Finger body: cols 14..16, rows 14..27 (extending below chin)
# Make it stand in front of the mouth and chin
finger_x0, finger_x1 = 14, 16
finger_y0, finger_y1 = 13, 28
# Finger fill (warm cream, slightly lighter at top)
for y in range(finger_y0, finger_y1 + 1):
    for x in range(finger_x0, finger_x1 + 1):
        set_px(x, y, FACE)
# Finger outline
for y in range(finger_y0, finger_y1 + 1):
    set_px(finger_x0 - 1, y, DARK)
    set_px(finger_x1 + 1, y, DARK)
# Top cap of finger (fingertip) with a tiny rounded shape
set_px(14, 13, FACE); set_px(15, 13, FACE); set_px(16, 13, FACE)
set_px(14, 12, FACE); set_px(15, 12, FACE); set_px(16, 12, FACE)
set_px(13, 12, DARK); set_px(17, 12, DARK)
set_px(13, 13, DARK); set_px(17, 13, DARK)
set_px(14, 11, DARK); set_px(15, 11, DARK); set_px(16, 11, DARK)
# Fingernail highlight near tip
set_px(15, 13, NAIL)
set_px(15, 14, NAIL)
# Bottom of finger fade-out (lower right shadow on finger)
for y in range(17, 28):
    set_px(16, y, FACE_SH)

# --- Subtle background "shh" wave hint: a few soft pixels of sound-mute on the right side
# Three small horizontal dashes representing muted sound waves, rendered in muted teal
WAVE = (127, 179, 165, 220)  # muted teal with slight transparency
wave_marks = [
    (27, 8), (28, 8), (29, 8),
    (28, 11), (29, 11), (30, 11),
    (27, 14), (28, 14), (29, 14),
]
for (x, y) in wave_marks:
    set_px(x, y, WAVE)
# Diagonal "mute" slash across the wave marks
for i in range(8):
    set_px(27 + i // 2, 7 + i, DARK)

# Render to PIL image
img = Image.new("RGBA", (W, H))
for y in range(H):
    for x in range(W):
        img.putpixel((x, y), px[y][x])

os.makedirs("src/Hush.VS/Resources", exist_ok=True)

# 128x128 marketplace icon (nearest-neighbor preserves pixels)
icon_128 = img.resize((128, 128), Image.NEAREST)
icon_128.save("src/Hush.VS/Resources/hush-icon.png", "PNG")

# 200x200 VSIX preview
icon_200 = img.resize((200, 200), Image.NEAREST)
icon_200.save("src/Hush.VS/Resources/hush-preview.png", "PNG")

# VSCode marketplace icon
icon_128.save("src/Hush.VSCode/icon.png", "PNG")

# Also keep the source 32x32 for posterity
img.save("src/Hush.VS/Resources/hush-icon-32.png", "PNG")

print("Wrote:")
print("  src/Hush.VS/Resources/hush-icon-32.png       (32x32 source)")
print("  src/Hush.VS/Resources/hush-icon.png          (128x128)")
print("  src/Hush.VS/Resources/hush-preview.png       (200x200)")
print("  src/Hush.VSCode/icon.png                     (128x128)")
