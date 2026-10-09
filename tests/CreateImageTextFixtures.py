"""Image-heavy pages with real text, to exercise the auxiliary engine's path."""
from io import BytesIO
from pathlib import Path

from PIL import Image
from pypdf import PdfWriter
from pypdf.generic import DecodedStreamObject, DictionaryObject, NameObject, NumberObject

root = Path(__file__).resolve().parents[1] / "artifacts" / "native-tests"
root.mkdir(parents=True, exist_ok=True)
for codec, pdf_filter in [("JPEG", "/DCTDecode"), ("JPEG2000", "/JPXDecode")]:
    writer = PdfWriter()
    page = writer.add_blank_page(600, 800)
    font = DictionaryObject({NameObject("/Type"): NameObject("/Font"), NameObject("/Subtype"): NameObject("/Type1"),
                             NameObject("/BaseFont"): NameObject("/Helvetica")})
    image = Image.new("RGB", (2048, 2048), (190, 210, 230))
    buffer = BytesIO()
    image.save(buffer, format=codec)
    pixels = DecodedStreamObject()
    pixels.set_data(buffer.getvalue())
    pixels.update({NameObject("/Type"): NameObject("/XObject"), NameObject("/Subtype"): NameObject("/Image"),
                   NameObject("/Width"): NumberObject(image.width), NameObject("/Height"): NumberObject(image.height),
                   NameObject("/BitsPerComponent"): NumberObject(8), NameObject("/ColorSpace"): NameObject("/DeviceRGB"),
                   NameObject("/Filter"): NameObject(pdf_filter)})
    page[NameObject("/Resources")] = DictionaryObject({
        NameObject("/Font"): DictionaryObject({NameObject("/F1"): writer._add_object(font)}),
        NameObject("/XObject"): DictionaryObject({NameObject("/Im1"): writer._add_object(pixels)})})
    content = DecodedStreamObject()
    content.set_data(b"/Span <</ActualText (Image replacement text)>> BDC\nq 500 0 0 500 50 100 cm /Im1 Do Q\nEMC\nBT /F1 20 Tf 60 720 Td (Selectable image-page text) Tj ET\n")
    page[NameObject("/Contents")] = writer._add_object(content)
    path = root / f"image-text-{codec.lower()}.pdf"
    writer.write(path)
    print(path)
