"""Known geometry fixtures for the production native text and export adapter."""
from pathlib import Path
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.cidfonts import UnicodeCIDFont
from pypdf import PdfReader, PdfWriter

root = Path(__file__).resolve().parents[1] / "artifacts" / "native-tests"
root.mkdir(parents=True, exist_ok=True)
source = root / "known-text.pdf"
c = canvas.Canvas(str(source), pagesize=(600, 800))
c.setFont("Helvetica", 20)
c.drawString(60, 720, "uwPDF selectable text")
c.saveState()
c.translate(100, 500)
c.rotate(30)
c.drawString(0, 0, "Rotated selection")
c.restoreState()
c.showPage()
pdfmetrics.registerFont(UnicodeCIDFont("STSong-Light"))
c.setFont("STSong-Light", 20)
c.drawString(60, 700, "中文选择与高亮测试")
c.showPage()
c.setFont("Helvetica", 20)
c.drawString(100, 600, "Crop and rotation")
c.showPage()
c.save()
reader = PdfReader(source)
writer = PdfWriter()
for page in reader.pages:
    writer.add_page(page)
writer.pages[2].cropbox.lower_left = (50, 100)
writer.pages[2].cropbox.upper_right = (550, 750)
writer.pages[2].rotate(90)
with source.open("wb") as output:
    writer.write(output)
print(source)
