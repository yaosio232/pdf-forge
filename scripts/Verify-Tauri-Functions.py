"""Real worker/supervisor regression. Never operates the application UI."""
import argparse, hashlib, json, os, pathlib, subprocess, time
from reportlab.pdfgen.canvas import Canvas
from reportlab.lib.pagesizes import A4
from reportlab.lib.utils import simpleSplit
from pypdf import PdfReader, PdfWriter
from worker_resources import runtime_directory

def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def fixture(path):
    canvas=Canvas(str(path),pagesize=A4)
    canvas.setTitle('Fresh forty-page functional fixture')
    canvas.setAuthor('Synthetic automated fixture')
    for n in range(1,41):
        canvas.bookmarkPage(f'page{n}')
        if n in [1,8,15,22,29,35]: canvas.addOutlineEntry(f'Chapter starting page {n}',f'page{n}')
        canvas.setFont('Helvetica-Bold',18);canvas.drawString(45,785,f'FORGE-PAGE-{n:02d}: Chapter {n}')
        canvas.setFont('Helvetica',11)
        paragraph=f'Unique page {n} contains source-protection evidence, continuous document content, and text with /Title and /Author literals that must survive metadata removal. '
        for row,line in enumerate(simpleSplit(paragraph*3,'Helvetica',11,500)): canvas.drawString(45,748-row*16,line)
        for row in range(7):
            y=560-row*23;canvas.rect(45,y,500,23);canvas.drawString(52,y+7,f'Item {n}-{row}    Quantity {n+row}    Price {(n+row)*3}.00')
        canvas.linkURL('https://example.com/forge', (45,300,300,320),relative=0)
        canvas.drawString(45,307,'External link')
        canvas.linkRect('Go to page 40','page40',(45,270,300,290));canvas.drawString(45,277,'Internal link to final page')
        canvas.drawString(45,45,f'Footer FORGE-PAGE-{n:02d}')
        canvas.showPage()
    canvas.save()

def main():
    parser=argparse.ArgumentParser();parser.add_argument('directory',type=pathlib.Path);parser.add_argument('runner',type=pathlib.Path);parser.add_argument('worker',type=pathlib.Path)
    args=parser.parse_args();root=args.directory.resolve();root.mkdir(parents=True,exist_ok=False)
    runtime=runtime_directory(args.worker,root)
    source=root/'source40.pdf';fixture(source);initial=digest(source);checks=[]
    def run(name,request,cancel=None,auto=False,active_cancel=False):
        path=root/f'{name}-request.json';result=root/f'{name}-result.json';path.write_text(json.dumps(request),encoding='utf-8')
        command=[str(args.runner.resolve()),str(path),str(args.worker.resolve()),str(result)]
        if cancel is not None:command.append(str(cancel))
        if auto:command.append('--auto')
        if active_cancel:command.append('--cancel-after-worker-start')
        environment={**os.environ,'PATH':str(runtime)+os.pathsep+os.environ['PATH']}
        completed=subprocess.run(command,capture_output=True,text=True,timeout=420,env=environment)
        if completed.returncode:raise RuntimeError(f'{name}: exit={completed.returncode} {completed.stderr}')
        return json.loads(result.read_text(encoding='utf-8'))
    parts=root/'parts';parts.mkdir();ends=[7,14,21,28,34,40];ranges=[];first=1
    for i,last in enumerate(ends):ranges.append(dict(first=first,last=last,name=f'part{i+1}.pdf'));first=last+1
    event=run('split',dict(kind='split',source=str(source),directory=str(parts),ranges=ranges,overwrite=False))
    assert event['status']=='completed',event
    for i,(start,end) in enumerate(zip([1,8,15,22,29,35],ends)):
        output=parts/f'part{i+1}.pdf';reader=PdfReader(output);assert len(reader.pages)==end-start+1
        for number,page in enumerate(reader.pages,start):
            text=page.extract_text();assert f'FORGE-PAGE-{number:02d}' in text and '/Title and /Author literals' in text and f'Item {number}-6' in text
            annotations=[ref.get_object() for ref in page.get('/Annots',[])]
            assert len(annotations)==(2 if end==40 else 1),annotations
            assert any(a.get('/A',{}).get('/URI')=='https://example.com/forge' for a in annotations)
            if end==40:
                targets=[a['/Dest'][0] for a in annotations if '/Dest' in a];assert len(targets)==1
                assert targets[0].indirect_reference==reader.pages[5].indirect_reference
        assert len(reader.outline)==1 and reader.get_destination_page_number(reader.outline[0])==0
        assert '/Author' not in (reader.metadata or {}) and '/Title' not in (reader.metadata or {})
        checks.append(dict(id=f'FUNC-SPLIT-{i+1}',status='PASS',pages=len(reader.pages),output=str(output),sha256=digest(output)))
    merged=root/'merged.pdf';event=run('merge',dict(kind='merge',sources=[str(parts/f'part{i}.pdf') for i in range(1,7)],output=str(merged),overwrite=False));assert event['status']=='completed',event
    reader=PdfReader(merged);assert len(reader.pages)==40
    for n,page in enumerate(reader.pages,1):assert f'FORGE-PAGE-{n:02d}' in page.extract_text()
    checks.append(dict(id='FUNC-MERGE',status='PASS',output=str(merged),sha256=digest(merged)))
    writer=PdfWriter(clone_from=source);writer.encrypt('known-user','known-owner',algorithm='AES-256');encrypted=root/'encrypted.pdf';writer.write(encrypted);encrypted_hash=digest(encrypted)
    unlock=root/'unlocked';unlock.mkdir();event=run('unlock',dict(kind='unlock',sources=[str(encrypted)],password='known-user',directory=str(unlock)));assert event['status']=='completed',event
    output=pathlib.Path(event['items'][0]['output']);reader=PdfReader(output);assert not reader.is_encrypted and len(reader.pages)==40
    for n,page in enumerate(reader.pages,1):assert f'FORGE-PAGE-{n:02d}' in page.extract_text()
    checks.append(dict(id='FUNC-UNLOCK',status='PASS',output=str(output),sha256=digest(output)))
    event=run('wrong-password',dict(kind='unlock',sources=[str(encrypted)],password='incorrect',directory=str(unlock)));assert event['status']=='failed',event
    checks.append(dict(id='FUNC-WRONG-PASSWORD',status='PASS',error=event['items'][0]['error']))
    before=digest(merged);event=run('overwrite-denied',dict(kind='merge',sources=[str(source)],output=str(merged),overwrite=False));assert event['status']=='rejected' and digest(merged)==before,event
    event=run('source-denied',dict(kind='merge',sources=[str(source)],output=str(source),overwrite=True));assert event['status']=='rejected' and digest(source)==initial,event
    event=run('cancel',dict(kind='split',source=str(source),directory=str(parts),ranges=ranges,overwrite=True),0);assert event['status']=='cancelled',event
    event=run('cancel-active-worker',dict(kind='merge',sources=[str(source)]*500,output=str(merged),overwrite=True),active_cancel=True);assert event['status']=='cancelled' and digest(merged)==before,event
    automatic_ranges=[dict(first=r['first'],last=r['last'],name=f'auto-{r["name"]}') for r in ranges]
    event=run('automatic-split',dict(kind='split',source=str(source),directory='',ranges=automatic_ranges,overwrite=False),auto=True);assert event['status']=='completed',event
    assert all(os.path.samefile(pathlib.Path(item['output']).parent,root) for item in event['items'])
    event=run('automatic-merge',dict(kind='merge',sources=[str(source)],output='',overwrite=False),auto=True);assert event['status']=='completed',event
    assert os.path.samefile(event['items'][0]['output'],root/'source40_merged.pdf')
    assert len(PdfReader(event['items'][0]['output']).pages)==40
    event=run('automatic-unlock',dict(kind='unlock',sources=[str(encrypted)],password='known-user',directory=''),auto=True);assert event['status']=='completed',event
    assert os.path.samefile(pathlib.Path(event['items'][0]['output']).parent,root)
    assert len(PdfReader(event['items'][0]['output']).pages)==40
    checks+= [dict(id=x,status='PASS') for x in ['FUNC-OVERWRITE-PROTECTION','FUNC-SOURCE-PROTECTION','FUNC-CANCEL']]
    checks+= [dict(id=x,status='PASS') for x in ['FUNC-ACTIVE-WORKER-CANCEL','FUNC-AUTOMATIC-SPLIT-OUTPUT','FUNC-AUTOMATIC-MERGE-OUTPUT','FUNC-AUTOMATIC-UNLOCK-OUTPUT']]
    assert digest(source)==initial and digest(encrypted)==encrypted_hash
    report=dict(workerExecutableSha256=digest(args.worker.resolve()),scope='real-function-and-supervised-worker-no-ui',sourceSha256=initial,encryptedSourceSha256=encrypted_hash,checks=checks)
    (root/'results.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2))
if __name__=='__main__':main()
