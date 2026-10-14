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
