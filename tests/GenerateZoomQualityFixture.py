"""Small text and fine vector line fixture, using the Python standard library."""
from pathlib import Path
import sys
root=Path(sys.argv[1]); root.mkdir(parents=True,exist_ok=True)
content=b'BT /F1 22 Tf 65 1300 Td (PDF zoom and sharpness) Tj ET\n'
for i,size in enumerate([6,8,10,12]):
    content+=f'BT /F1 {size} Tf 65 {1210-i*55} Td (Small text {size} pt: abcdefghijklmnopqrstuvwxyz 0123456789) Tj ET\n'.encode()
for i in range(20):
    content+=f'0.3 w 65 {880-i*6} m 760 {880-i*6} l S\n'.encode()
objects=[b'<< /Type /Catalog /Pages 2 0 R >>',b'<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
 b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1000 1400] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',
 b'<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
 b'<< /Length '+str(len(content)).encode()+b' >>\nstream\n'+content+b'\nendstream']
data=b'%PDF-1.4\n'; offsets=[0]
for i,obj in enumerate(objects,1):
    offsets.append(len(data));data+=str(i).encode()+b' 0 obj\n'+obj+b'\nendobj\n'
xref=len(data);data+=b'xref\n0 6\n0000000000 65535 f \n'+b''.join(f'{o:010d} 00000 n \n'.encode() for o in offsets[1:])
data+=f'trailer << /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n'.encode()
(root/'source.pdf').write_bytes(data)
