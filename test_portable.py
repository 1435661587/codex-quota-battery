from pathlib import Path
from unittest.mock import patch
import json,os,shutil,tempfile,unittest
import install_desktop

class PortableTests(unittest.TestCase):
    def test_installer_uses_current_environment_and_preserves_other_plugins(self):
        with tempfile.TemporaryDirectory() as td:
            base=Path(td);source=base/'source';source.mkdir();home=base/'home';home.mkdir();local=base/'local';local.mkdir()
            for name in ('QuotaBattery.cs','quota_reader.py','mcp_server_v3.py','README.md','recovery_monitor.py','LICENSE','plugin.json'):
                shutil.copy2(install_desktop.ROOT/name,source/name)
            mp=home/'.agents/plugins/marketplace.json';mp.parent.mkdir(parents=True)
            mp.write_text(json.dumps({'name':'personal-existing','plugins':[{'name':'another-plugin'}]}),'utf-8')
            calls=[]
            def fake_run(args,**kwargs):
                calls.append(args)
                if str(args[0]).endswith('csc.exe'):(Path(kwargs['cwd'])/'QuotaBattery.exe').write_bytes(b'fixture-exe')
                class Result:returncode=0
                return Result()
            env={'LOCALAPPDATA':str(local),'CODEX_HOME':str(home/'.codex'),'CODEX_THREAD_ID':'test-current-session','CODEX_MCP_NODE_PATH':'test-node','CODEX_APP_TOOLS_PIPE_PATH':'test-endpoint'}
            with patch.object(install_desktop,'ROOT',source),patch.dict(os.environ,env),patch.object(install_desktop.subprocess,'run',fake_run),patch.object(install_desktop.shutil,'which',return_value='codex'):
                dest=install_desktop.install(home=home,launch=False,enable_recovery=False)
            runtime=json.loads((dest/'recovery-runtime.json').read_text('utf-8'))
            self.assertEqual(runtime['callerThreadId'],'test-current-session')
            self.assertFalse(json.loads((local/'CodexQuotaBattery/recovery-settings.json').read_text())['enabled'])
            self.assertEqual(json.loads(mp.read_text())['plugins'][0]['name'],'another-plugin')
            self.assertIn('codex-quota-battery@personal-existing',calls[-1])
    def test_public_archive_excludes_runtime_logs_and_executables(self):
        import release
        for name in ['recovery-runtime.json','QuotaBattery.exe','desktop-live-test.json','test-data','node_modules']:
            self.assertNotIn(name,release.PUBLIC)
    def test_versions_match(self):
        root=Path(__file__).resolve().parent
        self.assertEqual(json.loads((root/'plugin.json').read_text('utf-8'))['version'],install_desktop.VERSION)

if __name__=='__main__':unittest.main()
