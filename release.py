"""Explicit checked publication, never a background upload watcher."""
from pathlib import Path
import argparse,json,re,subprocess,sys,zipfile
ROOT=Path(__file__).resolve().parent
PUBLIC=['.gitignore','AGENTS.md','README.md','LICENSE','CHANGELOG.md','VERSION','QuotaBattery.cs','quota_reader.py','recovery_monitor.py','mcp_server_v3.py','install.py','install_desktop.py','plugin.json','test_recovery.py','test_portable.py','release.py','preview-liquid.png','bilibili-description.md','bilibili-script.md','cover.jpg']
def check():
    version=(ROOT/'VERSION').read_text().strip()
    assert json.loads((ROOT/'plugin.json').read_text('utf-8'))['version']==version,'Manifest version mismatch'
    for name in PUBLIC:
        p=ROOT/name
        if not p.exists():continue
        if p.suffix in ('.png','.jpg'):continue
        text=p.read_text('utf-8-sig')
        for pattern in [r'C:[/\\]Users[/\\]Administrator',r'D:[/\\]AI',r'\b01[0-9a-f]{6}-[0-9a-f]{4}-7[0-9a-f]{3}-[0-9a-f]{4}-[0-9a-f]{12}\b',r'gh[pousr]_[A-Za-z0-9]{20,}',r'github_pat_[A-Za-z0-9_]{20,}',r'-----BEGIN (?:RSA |OPENSSH )?PRIVATE KEY-----']:
            if re.search(pattern,text):raise RuntimeError('Private/machine-specific content in '+name)
    subprocess.run([sys.executable,'-m','unittest','test_recovery','test_portable'],cwd=ROOT,check=True)
    print('Public source/privacy/tests passed.')
def pack():
    check();out=ROOT/'build';out.mkdir(exist_ok=True)
    target=out/('codex-quota-battery-v'+(ROOT/'VERSION').read_text().strip()+'.zip')
    with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as z:
        for name in PUBLIC:
            if (ROOT/name).exists():z.write(ROOT/name,name)
    print(target);return target
def sync(message):
    check()
    subprocess.run(['git','add','--']+[p for p in PUBLIC if (ROOT/p).exists()],cwd=ROOT,check=True)
    if subprocess.run(['git','diff','--cached','--quiet'],cwd=ROOT).returncode:
        subprocess.run(['git','commit','-m',message],cwd=ROOT,check=True)
    subprocess.run(['git','push','origin','HEAD:main'],cwd=ROOT,check=True)
    subprocess.run(['git','log','-1','--format=%h %s'],cwd=ROOT,check=True)
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('action',choices=['check','pack','sync']);p.add_argument('--message',default='Update quota widget');a=p.parse_args()
    {'check':check,'pack':pack,'sync':lambda:sync(a.message)}[a.action]()
