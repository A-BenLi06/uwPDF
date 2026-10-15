"""Create native reader QA fixtures using only the Python standard library."""
from pathlib import Path
import sys

def write_pdf(objects, target):
    data=b'%PDF-1.4\n'; offsets=[0]
    for i,obj in enumerate(objects,1):
        offsets.append(len(data)); data+=str(i).encode()+b' 0 obj\n'+obj+b'\nendobj\n'
    xref=len(data)
    data+=f'xref\n0 {len(offsets)}\n0000000000 65535 f \n'.encode()
    data+=b''.join(f'{o:010d} 00000 n \n'.encode() for o in offsets[1:])
    data+=f'trailer << /Size {len(offsets)} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n'.encode()
    target.write_bytes(data)
def stream(content, attributes=b''):
    return b'<< '+attributes+b' /Length '+str(len(content)).encode()+b' >>\nstream\n'+content+b'\nendstream'
root=Path(sys.argv[1]); image=(root/'scanned.jpg').read_bytes()
write_pdf([b'<< /Type /Catalog /Pages 2 0 R >>', b'<< /Type /Pages /Kids [3 0 R 6 0 R] /Count 2 >>',
 b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 700] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>',
 stream(image,b'/Type /XObject /Subtype /Image /Width 1000 /Height 1400 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode'),
 stream(b'q 500 0 0 700 0 0 cm /Im0 Do Q\n'),
 b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 700] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>'],root/'scanned.pdf')
write_pdf([b'<< /Type /Catalog /Pages 2 0 R >>',b'<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
 b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 700] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',
 b'<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
 stream(b'BT /F1 24 Tf 50 590 Td (Native text selection test) Tj 0 -50 Td (Move ink and restore saves) Tj ET\n')],root/'source.pdf')

objects=[b'<< /Type /Catalog /Pages 2 0 R >>',b'<< /Type /Pages /Kids [4 0 R 6 0 R 8 0 R 10 0 R 12 0 R] /Count 5 >>',b'<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>']
for number,label in enumerate(['PAGE_ONE','PAGE_TWO','PAGE_THREE','PAGE_FOUR','PAGE_FIVE']):
    content=f'BT /F1 16 Tf 40 650 Td ({label} boundary) Tj ET\n'.encode()
    for row in range(18):
        content+=f'BT /F1 12 Tf 40 {610-row*27} Td (Selection row {row+1} on {label} 0123456789) Tj ET\n'.encode()
    objects.append(f'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 700] /Resources << /Font << /F1 3 0 R >> >> /Contents {5+number*2} 0 R >>'.encode())
    objects.append(stream(content))
write_pdf(objects,root/'selection.pdf')
