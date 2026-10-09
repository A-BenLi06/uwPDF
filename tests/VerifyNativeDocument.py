"""Checks production text geometry and annotation export on known PDF fixtures.

Run scripts/Test-NativeText.ps1 first to compile the production adapter harness.
No application window is opened by this test.
"""
import argparse
import hashlib
import json
import math
from datetime import datetime, timezone
from pathlib import Path
import runpy
import subprocess

from pypdf import PdfReader

repo = Path(__file__).resolve().parents[1]
root = repo / "artifacts" / "native-tests"
parser = argparse.ArgumentParser()
parser.add_argument("--exe", type=Path, default=root / "NativeTextSmoke.exe")
parser.add_argument("--component", type=Path)
parser.add_argument("--output-root", type=Path, default=root)
args = parser.parse_args()
exe = args.exe.resolve()
output_root = args.output_root.resolve()
output_root.mkdir(parents=True, exist_ok=True)
command = [str(exe)] + ([str(args.component.resolve())] if args.component else [])
if not exe.exists():
    raise SystemExit(f"Build the native document harness first: {exe}")
runpy.run_path(str(repo / "tests" / "CreateTextFixtures.py"))
source = root / "known-text.pdf"
expected = ["uwPDF selectable text", "中文选择与高亮测试", "Crop and rotation"]
reports = []


def inspect(path, page, text, output):
    subprocess.run(command + [str(path), str(page), text, str(output)], check=True)
    data = json.loads(output.read_text(encoding="utf-8"))
    assert len(data["coordinates"]) == len(data["text"].encode("utf-16-le")) // 2 * 9
    assert all(math.isfinite(v) for v in data["coordinates"])
    return data


for page, text in enumerate(expected):
    reports.append(inspect(source, page, text, output_root / f"geometry-{page}.json"))

assert abs(reports[0]["coordinates"][0] - .1) < .0001
assert reports[0]["coordinates"][1] < .1 < reports[0]["coordinates"][5]
rotated = reports[0]["text"].index("Rotated selection") * 9
assert abs(reports[0]["coordinates"][rotated + 8] + math.pi / 6) < .0001
assert abs(reports[2]["coordinates"][8] - math.pi / 2) < .0001

# Export the actual native selection geometry for ordinary, CJK, angled,
# cropped and page-rotated text, rather than arbitrary test rectangles.
annotations = []
for page, text in [(0, expected[0]), (0, "Rotated selection"), (1, expected[1]), (2, expected[2])]:
    data = reports[page]
    start = data["text"].index(text)
    coords = data["coordinates"]
    first = coords[start * 9:start * 9 + 8]
    last = coords[(start + len(text) - 1) * 9:(start + len(text) - 1) * 9 + 8]
    quad = first[:2] + last[2:4] + first[4:6] + last[6:8]
    annotations.append(dict(page=page, kind="highlight", color=[1, 1, 0], opacity=.35, quads=[quad]))
annotations.append(dict(page=0, kind="ink", color=[0, .3, 1], opacity=1, width=.004,
                        points=[[.15, .7], [.35, .8], [.5, .72]]))
annotation_file = output_root / "geometry-annotations.json"
annotation_file.write_text(json.dumps(annotations, ensure_ascii=False), encoding="utf-8")
exported = output_root / "geometry-exported.pdf"
subprocess.run(command + [str(source), "0", expected[0], str(exported), str(annotation_file)], check=True)

original_pdf, result_pdf = PdfReader(source), PdfReader(exported)
assert len(result_pdf.pages) == len(original_pdf.pages)
for page, text in enumerate(expected):
    original, result = original_pdf.pages[page], result_pdf.pages[page]
    assert list(original.mediabox) == list(result.mediabox)
    assert list(original.cropbox) == list(result.cropbox)
    assert original.rotation == result.rotation
    extracted = inspect(exported, page, text, output_root / f"exported-geometry-{page}.json")
    assert extracted["text"] == reports[page]["text"]
    assert all(abs(a - b) < .00001 for a, b in zip(extracted["coordinates"], reports[page]["coordinates"]))
    assert len(result["/Annots"]) == [3, 1, 1][page]
    for reference in result["/Annots"]:
        annotation = reference.get_object()
        assert "/AP" in annotation and "/N" in annotation["/AP"]
        if annotation["/Subtype"] == "/Highlight":
            assert len(annotation["/QuadPoints"]) == 8
            assert abs(float(annotation["/CA"]) - .35) < .001
        else:
            assert annotation["/Subtype"] == "/Ink"
            assert len(annotation["/InkList"][0]) == 6
print("PASS: native text, known quads, CJK, crop, rotation, standard annotations, appearances, preserved content")
print(f"Visual inspection artifact: {exported}")

runpy.run_path(str(repo / "tests" / "CreateImageTextFixtures.py"))
for codec in ["jpeg", "jpeg2000"]:
    image_pdf = root / f"image-text-{codec}.pdf"
    data = inspect(image_pdf, 0, "Image replacement text", output_root / f"image-{codec}-geometry.json")
    assert "Selectable image-page text" in data["text"]
    # ActualText wrapped around an image needs image placement, but no decoded
    # pixels. The first character remains inside that image's page rectangle.
    assert .08 <= data["coordinates"][0] <= .92
    image_annotations = output_root / f"image-{codec}-annotations.json"
    image_annotations.write_text(json.dumps([dict(page=0, kind="ink", color=[0, .3, 1], opacity=1,
                                                 width=.004, points=[[.1, .1], [.4, .2]])]), encoding="utf-8")
    output = output_root / f"image-{codec}-exported.pdf"
    subprocess.run(command + [str(image_pdf), "0", "Selectable", str(output), str(image_annotations)], check=True)
    before, after = PdfReader(image_pdf).pages[0], PdfReader(output).pages[0]
    original_image = before["/Resources"]["/XObject"]["/Im1"].get_object()
    exported_image = after["/Resources"]["/XObject"]["/Im1"].get_object()
    assert original_image["/Filter"] == exported_image["/Filter"]
    assert original_image._data == exported_image._data
    assert after["/Annots"][0].get_object()["/Subtype"] == "/Ink"
print("PASS: JPEG/JPX text and image ActualText geometry; export preserves compressed image data")
inputs = [Path(__file__), repo / "tests/CreateTextFixtures.py", repo / "tests/CreateImageTextFixtures.py", exe,
          source, root / "image-text-jpeg.pdf", root / "image-text-jpeg2000.pdf"]
if args.component:
    inputs.append(args.component.resolve())
(output_root / "document-verification.json").write_text(json.dumps(dict(
    completed_utc=datetime.now(timezone.utc).isoformat(),
    input_sha256={str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs},
    output_sha256={str(path): hashlib.sha256(path.read_bytes()).hexdigest()
                   for path in [exported, output_root / "image-jpeg-exported.pdf", output_root / "image-jpeg2000-exported.pdf"]},
    scope="Actual native text/document DLL: UTF-16 quads, CJK, rotation/CropBox, text preservation, standard Ink/Highlight APs, JPEG/JPX compressed data and image ActualText; excludes XAML, device input and total memory"
), indent=2), encoding="utf-8")
