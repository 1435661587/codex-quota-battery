"""Official local MCP plugin controlling a directly visible draggable desktop widget."""
import ctypes,datetime,json,os,subprocess,sys,time
from pathlib import Path
from quota_reader import QuotaReader
from recovery_monitor import publish_runtime
ROOT=Path(__file__).resolve().parent
DATA=Path(os.environ['LOCALAPPDATA'])/'CodexQuotaBattery'
READER=QuotaReader()
def running():
    kernel=ctypes.WinDLL('kernel32',use_last_error=True)
    kernel.OpenMutexW.argtypes=[ctypes.c_ulong,ctypes.c_bool,ctypes.c_wchar_p];kernel.OpenMutexW.restype=ctypes.c_void_p
    kernel.CloseHandle.argtypes=[ctypes.c_void_p]
    handle=kernel.OpenMutexW(0x100000,False,'Local\\CodexQuotaBattery')
    if handle:kernel.CloseHandle(handle)
    return bool(handle)
def open_widget():
    # Use only the endpoint supplied by Codex; never discover or impersonate another client.
    config_path=ROOT/'recovery-runtime.json'
    try:
        config=json.loads(config_path.read_text('utf-8'))
        refreshed=publish_runtime(config)
        if refreshed!=config:
            temp=config_path.with_name(config_path.name+'.'+str(os.getpid())+'.tmp');temp.write_text(json.dumps(refreshed,ensure_ascii=False),'utf-8');os.replace(temp,config_path)
    except (OSError,ValueError):pass
    if not running():
        subprocess.Popen([str(ROOT/'QuotaBattery.exe')],cwd=ROOT,creationflags=subprocess.CREATE_NO_WINDOW)
    deadline=time.monotonic()+6
    while time.monotonic()<deadline:
        try:
            state=json.loads((DATA/'widget-state.json').read_text('utf-8-sig'))
            if running() and state.get('visible'):return state
        except (OSError,ValueError):pass
        time.sleep(.1)
    raise RuntimeError('挂件启动尚未确认，请检查本机状态。')
def quota():
    try:
        state=json.loads((DATA/'status.json').read_text('utf-8-sig'))
        updated=datetime.datetime.fromisoformat(state['updatedUtc'].replace('Z','+00:00'))
        if running() and (datetime.datetime.now(datetime.timezone.utc)-updated).total_seconds()<5:return state
    except (OSError,ValueError,KeyError):pass
    return READER.read()
def respond(method,params):
    if method=='initialize':
        try:open_widget()
        except (OSError,RuntimeError):pass
        return {'protocolVersion':params.get('protocolVersion','2025-06-18'),'capabilities':{'tools':{}},'serverInfo':{'name':'codex-quota-battery','version':'2.2.2'}}
    if method=='ping':return {}
    if method=='tools/list':return {'tools':[
        {'name':'open_quota_battery','description':'直接显示可拖动的桌面额度电池条，记住位置；官方通知收到即更新，并每2秒核对额度。不打开网页，不需要点击悬浮按钮。','inputSchema':{'type':'object','properties':{},'additionalProperties':False},'annotations':{'readOnlyHint':False,'destructiveHint':False,'openWorldHint':False}},
        {'name':'get_remaining_quota','description':'只读查看五小时和一周剩余额度，优先读取正在同步的桌面挂件，再读取官方额度接口。','inputSchema':{'type':'object','properties':{},'additionalProperties':False},'annotations':{'readOnlyHint':True,'openWorldHint':False}}
    ]}
    if method=='resources/list':return {'resources':[]}
    if method=='tools/call':
        name=params.get('name')
        try:
            if name=='open_quota_battery':
                state=open_widget();return {'content':[{'type':'text','text':'已显示桌面额度电池条。按住拖动，右键可刷新或退出。'}],'structuredContent':state}
            if name=='get_remaining_quota':
                data=quota();return {'content':[{'type':'text','text':json.dumps(data,ensure_ascii=False)}],'structuredContent':data}
        except (OSError,RuntimeError):return {'isError':True,'content':[{'type':'text','text':'本机额度挂件或额度连接暂不可用，请检查本机状态。'}]}
    raise ValueError('Unknown request')
try:
    for line in sys.stdin:
        try:
            request=json.loads(line)
            if 'id' not in request:continue
            try:result={'jsonrpc':'2.0','id':request['id'],'result':respond(request.get('method'),request.get('params') or {})}
            except (ValueError,KeyError):result={'jsonrpc':'2.0','id':request['id'],'error':{'code':-32602,'message':'Invalid request'}}
            print(json.dumps(result,ensure_ascii=True),flush=True)
        except (ValueError,KeyError):continue
finally:READER.close()

