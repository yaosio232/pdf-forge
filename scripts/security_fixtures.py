"""Generate public, synthetic 40-page security regression inputs locally."""
import hashlib
import json
import pathlib
import sys

from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.cidfonts import UnicodeCIDFont
from pypdf import PdfReader, PdfWriter


def generate(root):
    root.mkdir(parents=True, exist_ok=True)
    normal = root / 'rich-40.pdf'
    pdfmetrics.registerFont(UnicodeCIDFont('HeiseiMin-W3'))
    document = canvas.Canvas(str(normal), pagesize=(595, 842), pageCompression=1)
    for page in range(1, 41):
        document.setFont('Helvetica-Bold', 18)
        document.drawString(48, 790, f'PAGE-{page:03d} Security regression')
        document.setFont('HeiseiMin-W3', 14)
        document.drawString(48, 760, '日本語・繁體中文：字型與頁面驗證')
        document.setFont('Helvetica', 11)
        for row in range(12):
            document.drawString(48, 720-row*22, f'Page {page:03d}, paragraph {row+1:02d}: unique local test content and source protection.')
        document.setFont('Helvetica-Bold', 11)
        for row in range(5):
            y = 400-row*24
            document.rect(48, y, 480, 24)
            document.drawString(58, y+8, f'RECORD-{page:03d}-{row:02d}    Amount: {page*100+row}    State: synthetic')
        document.showPage()
    document.save()
    script_pdf = root / 'script-marker-40.pdf'
    writer = PdfWriter(clone_from=str(normal))
    writer.add_js('globalThis.__pdfScriptRan = true;')
    writer.write(script_pdf)
    manifest = []
    for path in (normal, script_pdf):
        reader = PdfReader(path)
        assert len(reader.pages) == 40
        for number, page in enumerate(reader.pages, 1):
            assert f'PAGE-{number:03d}' in page.extract_text()
        manifest.append(dict(file=path.name, pages=40, sha256=hashlib.sha256(path.read_bytes()).hexdigest(), license='generated-test-fixture'))
    (root/'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')


if __name__ == '__main__':
    generate(pathlib.Path(sys.argv[1]))
