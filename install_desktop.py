"""Portable Windows source installer; use inside Codex Desktop."""
from pathlib import Path
import argparse,json,os,shutil,subprocess,sys,uuid
from recovery_monitor import publish_runtime
ROOT=Path(__file__).resolve().parent
VERSION='2.2.2'
def load(path,default=None):
    try:return json.loads(path.read_text('utf-8-sig'))
    except (OSError,ValueError):return default
def install(home=None,launch=True,enable_recovery=False):
    if os.name!='nt':raise RuntimeError('Windows 10/11 only.')
    home=Path(home or Path.home());ch=Path(os.environ.get('CODEX_HOME',home/'.codex'))
    dest=home/'.agents/plugins/codex-quota-battery';dest.mkdir(parents=True,exist_ok=True)
    compiler=Path(os.environ.get('WINDIR','C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    if not compiler.exists():raise RuntimeError('Windows .NET Framework compiler not found.')
    build=ROOT/'build';build.mkdir(exist_ok=True);shutil.copy2(ROOT/'QuotaBattery.cs',build/'QuotaBattery.cs')
    subprocess.run([str(compiler),'/nologo','/target:winexe','/out:QuotaBattery.exe','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','QuotaBattery.cs'],cwd=build,check=True)
    owned=str(dest/'QuotaBattery.exe').replace("'","''")
    command="Get-Process QuotaBattery -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq '"+owned+"'} | ForEach-Object {taskkill /PID $_.Id /T /F | Out-Null}"
    subprocess.run(['powershell','-NoProfile','-Command',command],capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW)
    shutil.copy2(build/'QuotaBattery.exe',dest/'QuotaBattery.exe')
    for name in ('QuotaBattery.cs','quota_reader.py','mcp_server_v3.py','README.md','recovery_monitor.py','LICENSE'):shutil.copy2(ROOT/name,dest/name)
    servers=list((ch/'plugins/cache/openai-bundled/codex-app-tools').glob('*/server.mjs'))
    old=load(dest/'recovery-runtime.json',{})
    node=os.environ.get('CODEX_MCP_NODE_PATH') or shutil.which('node') or old.get('node')
    runtime={'python':str(Path(sys.base_prefix)/'python.exe'),'node':node,'server':str(max(servers,key=lambda p:p.stat().st_mtime)) if servers else old.get('server'),'pipe':os.environ.get('CODEX_APP_TOOLS_PIPE_PATH') or old.get('pipe'),'callerThreadId':os.environ.get('CODEX_THREAD_ID') or old.get('callerThreadId')}
    (dest/'recovery-runtime.json').write_text(json.dumps(runtime,ensure_ascii=False,indent=2),'utf-8')
    data=Path(os.environ['LOCALAPPDATA'])/'CodexQuotaBattery';data.mkdir(exist_ok=True)
    publish_runtime(runtime,data)
    settings=data/'recovery-settings.json'
    if enable_recovery or not settings.exists():settings.write_text(json.dumps({'enabled':bool(enable_recovery),'generation':str(uuid.uuid4())}),'utf-8')
    manifest=load(ROOT/'plugin.json');(dest/'plugin.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),'utf-8')
    server={'command':runtime['python'],'args':[str(dest/'mcp_server_v3.py')]}
    (dest/'mcp.json').write_text(json.dumps({'mcpServers':{'quota_battery':{'type':'stdio',**server}}},ensure_ascii=False,indent=2),'utf-8')
    (dest/'.mcp.json').write_text(json.dumps({'mcpServers':{'quota_battery':server}},ensure_ascii=False,indent=2),'utf-8')
    compat=dest/'.codex-plugin';compat.mkdir(exist_ok=True)
    (compat/'plugin.json').write_text(json.dumps({'name':'codex-quota-battery','version':VERSION,'description':manifest['description'],'mcpServers':'./.mcp.json','interface':manifest['extensions']['com.openai']['interface']},ensure_ascii=False,indent=2),'utf-8')
    mp=home/'.agents/plugins/marketplace.json';market=load(mp,{'name':'codex-personal','plugins':[]})
    entry={'name':'codex-quota-battery','source':{'source':'local','path':'./.agents/plugins/codex-quota-battery'},'policy':{'installation':'AVAILABLE','authentication':'ON_INSTALL'},'category':'Productivity'}
    market['plugins']=[x for x in market.get('plugins',[]) if x.get('name')!='codex-quota-battery']+[entry]
    mp.write_text(json.dumps(market,ensure_ascii=False,indent=2),'utf-8')
    exe=shutil.which('codex')
    if not exe:
        choices=list((Path(os.environ['LOCALAPPDATA'])/'OpenAI/Codex/bin').glob('*/codex.exe'));exe=str(max(choices,key=lambda p:p.stat().st_mtime)) if choices else None
    if not exe:raise RuntimeError('Install, open, and sign into Codex Desktop first.')
    subprocess.run([exe,'plugin','add','codex-quota-battery@'+market['name']],check=True)
    if launch:subprocess.Popen([str(dest/'QuotaBattery.exe')],cwd=dest,creationflags=subprocess.CREATE_NO_WINDOW)
    print('Installed Codex Quota Battery '+VERSION+' at '+str(dest))
    if not runtime.get('callerThreadId') or not runtime.get('pipe'):print('Auto-resume needs a Codex Desktop invocation to connect its task tools.')
    return dest
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--enable-recovery',action='store_true');p.add_argument('--no-launch',action='store_true');a=p.parse_args();install(launch=not a.no_launch,enable_recovery=a.enable_recovery)
