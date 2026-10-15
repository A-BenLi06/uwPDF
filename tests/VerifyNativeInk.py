"""Verifies system-generated pressure outlines inside standard PDF Ink APs."""
import json
import argparse
import hashlib
import math
from datetime import datetime, timezone
from pathlib import Path
import runpy
import subprocess

from PIL import Image, ImageDraw
from pypdf import PdfReader

repo = Path(__file__).resolve().parents[1]
root = repo / "artifacts" / "native-tests"
runpy.run_path(str(repo / "tests" / "CreateTextFixtures.py"))
parser = argparse.ArgumentParser()
parser.add_argument("--outlines", type=Path, default=root / "ink-outlines.json")
parser.add_argument("--exe", type=Path, default=root / "NativeTextSmoke.exe")
parser.add_argument("--component", type=Path)
parser.add_argument("--output-root", type=Path, default=root)
args = parser.parse_args()
outline_path = args.outlines.resolve()
output_root = args.output_root.resolve()
output_root.mkdir(parents=True, exist_ok=True)
command = [str(args.exe.resolve())] + ([str(args.component.resolve())] if args.component else [])
outlines = json.loads(outline_path.read_text())
print(f"Verifying ink outlines from {outline_path}")
assert len(outlines) == 3
assert outlines[0]["outline"] != outlines[1]["outline"]
for item in outlines:
    assert any(command[0] == "h" for command in item["outline"])
    assert all(math.isfinite(n) for command in item["outline"] for n in command[1:])

annotations = []
for page in [0, 2]:
    for kind, appearance in enumerate(outlines):
        item = dict(appearance, page=page, kind="ink", width=.04 if kind < 2 else .067, highlighter=kind == 2)
        points = [(x, y + kind * 100) for x, y in [(30, 80), (80, 20), (140, 90), (200, 40), (260, 80)]]
        if kind == 1:
            points = [(.9*x - .1*y + 20, .1*x + .9*y + 10) for x, y in points]
        item["points"] = [[x / 300, y / 400] for x, y in points]
        annotations.append(item)
annotation_file = output_root / "ink-annotations.json"
annotation_file.write_text(json.dumps(annotations), encoding="utf-8")
exported = output_root / "ink-exported.pdf"
subprocess.run(command + [str(root / "known-text.pdf"), "0", "uwPDF",
                str(exported), str(annotation_file), "batch"], check=True)
pdf = PdfReader(exported)
for page in [0, 2]:
    assert len(pdf.pages[page]["/Annots"]) == 3
    for kind, reference in enumerate(pdf.pages[page]["/Annots"]):
        annotation = reference.get_object()
        assert annotation["/Subtype"] == "/Ink"
        assert len(annotation["/InkList"][0]) == 10
        appearance = annotation["/AP"]["/N"].get_object()
        stream = appearance.get_data()
        assert b"f Q" in stream or b"f* Q" in stream
        assert b" S\n" not in stream
        assert len(stream) > 1000
        assert "/ExtGState" in appearance["/Resources"]
        if kind == 2:
            assert appearance["/Resources"]["/ExtGState"]["/GS"]["/BM"] == "/Darken"

subprocess.run(["pdftoppm", "-cropbox", "-scale-to", "1000", "-png", str(exported), str(output_root / "ink-exported")], check=True)

# Compare page-space geometry with rendered PDF pixels, including the rotated
# CropBox page. The mask threshold allows edge antialiasing differences.
def polygons(commands):
    points = []
    current = None
    for command in commands:
        op, coordinates = command[0], command[1:]
        if op == "m":
            points = [tuple(coordinates)]
            current = points[0]
        elif op == "l":
            current = tuple(coordinates)
            points.append(current)
        elif op == "c":
            a, b, c, d = current, coordinates[:2], coordinates[2:4], coordinates[4:6]
            for i in range(1, 17):
                t = i / 16
                points.append(tuple((1-t)**3*a[j] + 3*(1-t)**2*t*b[j] + 3*(1-t)*t*t*c[j] + t**3*d[j] for j in range(2)))
            current = tuple(d)
        elif op == "h":
            if len(points) >= 3:
                yield points
            points = []

alignment = {}
for page in [0, 2]:
    rendered = Image.open(output_root / f"ink-exported-{page + 1}.png").convert("RGB")
    width, height = rendered.size
    expected = Image.new("1", rendered.size)
    draw = ImageDraw.Draw(expected)
    for item in outlines[:2]:
        for polygon in polygons(item["outline"]):
            draw.polygon([(x * width, y * height) for x, y in polygon], fill=1)
    pixels = rendered.tobytes()
    actual = [(b > 170 and r < 100 and g < 160) for r, g, b in zip(pixels[0::3], pixels[1::3], pixels[2::3])]
    target = expected.convert("L").tobytes()
    intersection = sum(bool(a) and bool(b) for a, b in zip(actual, target))
    union = sum(bool(a) or bool(b) for a, b in zip(actual, target))
    ratio = intersection / union
    alignment[str(page + 1)] = ratio
    assert ratio > .85, (page, ratio)
    print(f"PASS: Ink outline/page alignment, page {page + 1}, pixel-mask IoU {ratio:.3f}")
print("PASS: pressure/constant-width/rectangular pen outlines, batch export, standard InkList, filled appearances")
inputs = [Path(__file__), repo / "tests/CreateTextFixtures.py", outline_path, args.exe.resolve(), root / "known-text.pdf"]
if args.component:
    inputs.append(args.component.resolve())
(output_root / "ink-verification.json").write_text(json.dumps(dict(
    completed_utc=datetime.now(timezone.utc).isoformat(),
    input_sha256={str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs},
    exported_sha256=hashlib.sha256(exported.read_bytes()).hexdigest(), pixel_mask_iou=alignment,
    scope="Actual annotation writer DLL: batch export, rotated CropBox, pressure/constant-width/rectangular ink filled APs and standard InkList; compares exported raster geometry; excludes interactive pen latency and XAML"
), indent=2), encoding="utf-8")
