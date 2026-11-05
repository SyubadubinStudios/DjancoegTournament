"""
Builds the marketing / press-kit images in docs/marketing from the text-free key art in docs/marketing/art
and the game screenshots. Lettering uses the game's own fonts (assets/fonts), so it is always legible and correct.

    python tools/marketing/make_marketing.py

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.
"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ART = os.path.join(ROOT, "docs", "marketing", "art")
OUT = os.path.join(ROOT, "docs", "marketing")
SHOTS = os.path.join(ROOT, "docs", "screenshots")
FONTS = os.path.join(ROOT, "assets", "fonts")

KUNING, CABE, KAPUR, ASPAL, TARP = (255, 194, 26), (227, 36, 43), (247, 243, 232), (20, 17, 15), (29, 63, 187)

TEXT = {
    "en": {
        "tagline": "Ultimate Indonesian Chaos Battle Arena",
        "credit": "Made by Ariana Mischa Fadhila of Syubadubin Studios",
        "features": ["20-stage island campaign", "6 boss fights", "Co-op & online", "Level editor & mods"],
        "cta": "FREE DOWNLOAD  ·  Windows · Linux · macOS",
        "collage": "Fight across the Indonesian archipelago",
        "captions": ["Tur Nusantara: island campaign", "Boss fights", "Level editor", "Illustrated, narrated story",
                     "First & third person", "Monsters crash every match"],
    },
    "id": {
        "tagline": "Arena tawuran paling rusuh se-Nusantara",
        "credit": "Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios",
        "features": ["Kampanye 20 tahap antarpulau", "6 pertarungan bos", "Main bareng & online", "Editor arena & mod"],
        "cta": "UNDUH GRATIS  ·  Windows · Linux · macOS",
        "collage": "Tawuran dari Sabang sampai Nusa Tenggara",
        "captions": ["Tur Nusantara: kampanye antarpulau", "Pertarungan bos", "Editor arena", "Cerita bergambar & bernarasi",
                     "Orang pertama & ketiga", "Makhluk aneh menyerbu"],
    },
}
COLLAGE_SHOTS = ["campaign-{lang}.png", "boss-id.png", "editor-{lang}.png", "cutscene-id.png", "game-tps-en-stasiun-kereta-race.png", "creeps-id.png"]


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name), size)


def cover(img, w, h, focus_y=0.5):
    """Scales and crops `img` to exactly w x h (like CSS background-size: cover)."""
    s = max(w / img.width, h / img.height)
    r = img.resize((round(img.width * s), round(img.height * s)), Image.LANCZOS)
    x = (r.width - w) // 2
    y = int((r.height - h) * focus_y)
    return r.crop((x, y, x + w, y + h))


def trim_flat(img, threshold=14):
    """Removes flat border bands (empty margins some generated art comes with)."""
    g = img.convert("L")
    w, h = g.size
    def flat_row(y):
        row = [g.getpixel((x, y)) for x in range(0, w, 8)]
        return max(row) - min(row) < threshold
    def flat_col(x):
        col = [g.getpixel((x, y)) for y in range(0, h, 8)]
        return max(col) - min(col) < threshold
    top, bottom, left, right = 0, h - 1, 0, w - 1
    while top < h // 4 and flat_row(top): top += 1
    while bottom > h * 3 // 4 and flat_row(bottom): bottom -= 1
    while left < w // 4 and flat_col(left): left += 1
    while right > w * 3 // 4 and flat_col(right): right -= 1
    return img.crop((left, top, right + 1, bottom + 1))


def shade(img, top=0.0, bottom=0.0, top_frac=0.35, bottom_frac=0.3):
    """Darkens the top and/or bottom edge so lettering stays readable."""
    w, h = img.size
    over = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(over)
    th, bh = int(h * top_frac), int(h * bottom_frac)
    for y in range(th):
        d.line([(0, y), (w, y)], fill=(10, 8, 20, int(255 * top * (1 - y / th) ** 1.4)))
    for y in range(bh):
        d.line([(0, h - 1 - y), (w, h - 1 - y)], fill=(10, 8, 20, int(255 * bottom * (1 - y / bh) ** 1.4)))
    return Image.alpha_composite(img.convert("RGBA"), over)


def sign_text(img, text, center, size, fill=KUNING, shadow=CABE, fontname="Bungee-Regular.ttf", stroke=None):
    """Sign-painter lettering like the in-game title: dark outline, offset coloured shadow, bright face."""
    d = ImageDraw.Draw(img)
    f = font(fontname, size)
    stroke = stroke if stroke is not None else max(2, size // 14)
    bbox = d.textbbox((0, 0), text, font=f, stroke_width=stroke)
    w, h = bbox[2] - bbox[0], bbox[3] - bbox[1]
    x, y = center[0] - w / 2 - bbox[0], center[1] - h / 2 - bbox[1]
    off = max(3, size // 12)
    # soft drop shadow
    glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(glow).text((x + off * 1.6, y + off * 1.6), text, font=f, fill=(0, 0, 0, 170), stroke_width=stroke, stroke_fill=(0, 0, 0, 170))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(off)))
    d = ImageDraw.Draw(img)
    if shadow is not None:
        d.text((x + off, y + off), text, font=f, fill=shadow, stroke_width=stroke, stroke_fill=ASPAL)
    d.text((x, y), text, font=f, fill=fill, stroke_width=stroke, stroke_fill=ASPAL)
    return w, h


def plain_text(img, text, center, size, fill=KAPUR, fontname="PlusJakartaSans-ExtraBold.ttf", stroke=0, anchor="mm"):
    d = ImageDraw.Draw(img)
    f = font(fontname, size)
    d.text(center, text, font=f, fill=fill, anchor=anchor, stroke_width=stroke, stroke_fill=ASPAL)


def ribbon(img, text, center, size, bg=CABE, fg=KAPUR, pad=0.55):
    """Text on a tilted-looking solid strip (the game's sticker style, kept straight for print)."""
    d = ImageDraw.Draw(img)
    f = font("PlusJakartaSans-ExtraBold.ttf", size)
    b = d.textbbox((0, 0), text, font=f)
    w, h = b[2] - b[0], b[3] - b[1]
    px, py = int(size * pad * 1.6), int(size * pad)
    x0, y0 = center[0] - w / 2 - px, center[1] - h / 2 - py
    d.rectangle([x0 + size * 0.18, y0 + size * 0.18, x0 + w + 2 * px + size * 0.18, y0 + h + 2 * py + size * 0.18], fill=ASPAL)
    d.rectangle([x0, y0, x0 + w + 2 * px, y0 + h + 2 * py], fill=bg)
    d.text((center[0], center[1]), text, font=f, fill=fg, anchor="mm")


def title_block(img, cx, top, size, lang):
    """DJANCOEG / TOURNAMENT wordmark plus tagline; returns the y below it."""
    _, h1 = sign_text(img, "DJANCOEG", (cx, top + size * 0.55), size)
    _, h2 = sign_text(img, "TOURNAMENT", (cx, top + size * 0.55 + h1 * 0.5 + size * 0.42), int(size * 0.44), fill=KAPUR, shadow=TARP)
    y = top + size * 0.55 + h1 * 0.5 + size * 0.42 + h2 * 0.5 + size * 0.28
    plain_text(img, TEXT[lang]["tagline"], (cx, y), int(size * 0.22), stroke=max(1, size // 60))
    return y + size * 0.2


def credit(img, lang, size, pos="br", margin=None):
    w, h = img.size
    margin = margin or int(size * 1.2)
    f = font("SpaceMono-Bold.ttf", size)
    d = ImageDraw.Draw(img)
    t = TEXT[lang]["credit"]
    anchor = {"br": "rd", "bc": "md", "bl": "ld"}[pos]
    x = {"br": w - margin, "bc": w // 2, "bl": margin}[pos]
    d.text((x, h - margin), t, font=f, fill=(247, 243, 232, 230), anchor=anchor, stroke_width=2, stroke_fill=ASPAL)


def save(img, name):
    path = os.path.join(OUT, name)
    img.convert("RGB").save(path, quality=92) if name.endswith(".jpg") else img.save(path)
    print("wrote", os.path.relpath(path, ROOT), img.size)


def load(name):
    return Image.open(os.path.join(ART, name)).convert("RGBA")


def logo(w=1280, h=720):
    """Transparent wordmark (Steam library logo, overlays, video end cards)."""
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    sign_text(img, "DJANCOEG", (w // 2, int(h * 0.40)), int(h * 0.30))
    sign_text(img, "TOURNAMENT", (w // 2, int(h * 0.72)), int(h * 0.13), fill=KAPUR, shadow=TARP)
    return img


def main():
    wide, square, vertical, library = (trim_flat(load(n)) for n in ("wide.jpg", "square.jpg", "vertical.jpg", "library.jpg"))

    for lang in ("en", "id"):
        # Web / social banner 1920x820 (README header, Twitter/X header, itch.io banner).
        b = shade(cover(wide, 1920, 820, 0.35), top=0.85, bottom=0.6, top_frac=0.42, bottom_frac=0.25)
        title_block(b, 960, 18, 150, lang)
        credit(b, lang, 20)
        save(b, f"banner-1920x820-{lang}.jpg")

        # Instagram / Facebook square 1080x1080.
        s = shade(cover(square, 1080, 1080, 0.5), top=0.9, bottom=0.9, top_frac=0.33, bottom_frac=0.3)
        title_block(s, 540, 26, 140, lang)
        feats = TEXT[lang]["features"]
        ribbon(s, "  ·  ".join(feats[:2]), (540, 925), 30, bg=KUNING, fg=ASPAL)
        ribbon(s, "  ·  ".join(feats[2:]), (540, 990), 30)
        credit(s, lang, 15, "bc", margin=14)
        save(s, f"social-1080-{lang}.jpg")

        # Stories / TikTok / Reels 1080x1920.
        v = shade(cover(vertical, 1080, 1920, 0.6), top=0.9, bottom=0.95, top_frac=0.32, bottom_frac=0.36)
        title_block(v, 540, 70, 170, lang)
        y = 1460
        for i, feat in enumerate(feats):
            ribbon(v, feat, (540, y + i * 82), 36, bg=(KUNING if i % 2 == 0 else CABE), fg=(ASPAL if i % 2 == 0 else KAPUR))
        plain_text(v, TEXT[lang]["cta"], (540, 1810), 34, fill=KUNING, stroke=2)
        credit(v, lang, 17, "bc", margin=30)
        save(v, f"story-1080x1920-{lang}.jpg")

        # Feature collage 1920x1080 from real screenshots.
        c = Image.new("RGBA", (1920, 1080), TARP + (255,))
        cd = ImageDraw.Draw(c)
        for yy in range(0, 1080, 6):
            cd.line([(0, yy), (1920, yy)], fill=(24, 55, 170, 255))
        sign_text(c, TEXT[lang]["collage"].upper(), (960, 70), 52)
        gx, gy, cw, ch, gap = 60, 140, 580, 326, 40
        for i, shot in enumerate(COLLAGE_SHOTS):
            p = os.path.join(SHOTS, shot.format(lang=lang))
            if not os.path.exists(p):
                p = os.path.join(SHOTS, shot.format(lang="en" if lang == "id" else "id"))
            im = cover(Image.open(p).convert("RGBA"), cw, ch)
            x = gx + (i % 3) * (cw + gap)
            y = gy + (i // 3) * (ch + 120)
            cd.rectangle([x + 10, y + 10, x + cw + 10, y + ch + 10], fill=ASPAL)
            cd.rectangle([x - 6, y - 6, x + cw + 6, y + ch + 6], fill=KAPUR)
            c.alpha_composite(im, (x, y))
            ribbon(c, TEXT[lang]["captions"][i], (x + cw // 2, y + ch + 46), 24, bg=(KUNING if i % 2 == 0 else CABE), fg=(ASPAL if i % 2 == 0 else KAPUR))
        credit(c, lang, 18, "bc", margin=16)
        save(c, f"features-1920x1080-{lang}.jpg")

    # Steam store / library assets (text-free hero per Steam rules; capsules carry the wordmark).
    def capsule(w, h, art, focus, size, tag=False):
        im = shade(cover(art, w, h, focus), top=0.75, bottom=0.55 if tag else 0.0, top_frac=0.6, bottom_frac=0.35)
        sign_text(im, "DJANCOEG", (w // 2, int(h * 0.28)), size)
        sign_text(im, "TOURNAMENT", (w // 2, int(h * 0.28 + size * 0.88)), int(size * 0.42), fill=KAPUR, shadow=TARP)
        return im

    save(capsule(460, 215, wide, 0.4, 62), "steam/header-capsule-460x215.png")
    save(capsule(616, 353, wide, 0.4, 84, tag=True), "steam/main-capsule-616x353.png")
    save(capsule(231, 87, wide, 0.35, 30), "steam/small-capsule-231x87.png")
    lib = shade(cover(library, 600, 900, 0.5), top=0.85, bottom=0.6, top_frac=0.3, bottom_frac=0.25)
    sign_text(lib, "DJANCOEG", (300, 92), 92)
    sign_text(lib, "TOURNAMENT", (300, 172), 40, fill=KAPUR, shadow=TARP)
    save(lib, "steam/library-capsule-600x900.jpg")
    save(cover(wide, 3840, 1240, 0.45), "steam/library-hero-3840x1240.jpg")
    save(logo(), "steam/library-logo-1280x720.png")
    save(logo(), "logo-transparent-1280x720.png")


if __name__ == "__main__":
    os.makedirs(os.path.join(OUT, "steam"), exist_ok=True)
    main()
