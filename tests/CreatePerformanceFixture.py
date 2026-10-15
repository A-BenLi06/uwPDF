"""Deterministic 600-page image/text fixture for engine and app acceptance."""
from pathlib import Path
import random

from PIL import Image
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas

root = Path(__file__).resolve().parents[1] / "artifacts" / "native-tests"
root.mkdir(parents=True, exist_ok=True)
rng = random.Random(42)
images = []
for index in range(40):
    image = Image.frombytes("RGB", (1000, 1400), rng.randbytes(1000 * 1400 * 3))
    path = root / f"performance-{index:02d}.jpg"
    image.save(path, quality=62)
    images.append(ImageReader(str(path)))

path = root / "performance-600.pdf"
pdf = canvas.Canvas(str(path), pagesize=(595.28, 841.89), pageCompression=1, invariant=1)
for index in range(600):
    pdf.drawImage(images[index % 40], 24, 80, 547, 715)
    pdf.setFont("Helvetica", 18)
    pdf.drawString(30, 810, f"Performance fixture page {index + 1} / 600")
    pdf.setFont("Helvetica", 10)
    for row in range(5):
        pdf.drawString(30, 60 - row * 11, f"Selectable PDF text row {row + 1}, page {index + 1}")
    pdf.showPage()
pdf.save()
print(f"Created {path}: 600 pages, 40 unique JPEG images, {path.stat().st_size} bytes")
