using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

// Local companion: read-only account/rateLimits/read. Never starts an AI turn.
static class Program {
    internal static readonly string Data = Environment.CommandLine.Contains("--self-test") ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuotaBattery");
    internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    [STAThread] static void Main(string[] args) {
        Directory.CreateDirectory(Data);
        try {
            if (args.Contains("--self-test")) { Tests(); return; }
            int demoIndex=Array.IndexOf(args,"--demo-export");
            if(demoIndex>=0&&demoIndex+1<args.Length){Application.EnableVisualStyles();using(var demo=new BatteryForm())demo.PreviewDemo(args[demoIndex+1]);return;}
            if (args.Contains("--probe")) {
                using (var rpc = new QuotaRpc()) { var q=rpc.Read(); q.Save(); }
                return;
            }
            int previewIndex=Array.IndexOf(args,"--snapshot");
            if(previewIndex>=0 && previewIndex+1<args.Length) {
                Application.EnableVisualStyles();
                using(var preview=new BatteryForm())preview.Preview(args[previewIndex+1]);
                return;
            }
            bool created;
            using (var mutex = new Mutex(true,"Local\\CodexQuotaBattery",out created)) {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var form = new BatteryForm()) {
                    int snapshot=Array.IndexOf(args,"--snapshot");
                    if(snapshot>=0 && snapshot+1<args.Length) { form.Preview(args[snapshot+1]); return; }
                    Application.Run(form);
                }
            }
        } catch(Exception e) { File.WriteAllText(Path.Combine(Data,"error.log"),e.GetType().Name+": "+e.Message,Encoding.UTF8); Environment.ExitCode=1; }
    }
    static void Tests() {
        if(Quota.Remaining(46)!=54 || Quota.Remaining(140)!=0 || Quota.Remaining(-20)!=100)throw new Exception("Percentage normalization failed");
        if(BatteryForm.LitSegments(0)!=0||BatteryForm.LitSegments(1)!=1||BatteryForm.LitSegments(35)!=4||BatteryForm.LitSegments(100)!=10)throw new Exception("Segment mapping failed");
        var low=BatteryForm.BarColor(5);var high=BatteryForm.BarColor(95);
        if(low.R<=low.G || high.G<=high.R)throw new Exception("Battery color interpolation failed");
        var parsed=Quota.Parse("{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":46,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":7,\"windowDurationMins\":10080}}}}");
        if(parsed.ShortRemaining!=54 || parsed.WeekRemaining!=93)throw new Exception("Window mapping failed");
        var missing=Quota.Parse("{\"rateLimits\":{\"primary\":null,\"secondary\":null}}");
        if(missing.ShortRemaining!=null || missing.WeekRemaining!=null)throw new Exception("Missing limit treated as available");
        using(var rpc=new QuotaRpc()) {
            Quota pushed=null;rpc.Updated+=q=>pushed=q;
            rpc.HandleNotification(Quota.Obj(new JavaScriptSerializer().DeserializeObject("{\"method\":\"account/rateLimits/updated\",\"params\":{\"rateLimits\":{\"primary\":{\"usedPercent\":31,\"windowDurationMins\":300}}}}")));
            if(pushed==null||pushed.ShortRemaining!=69)throw new Exception("Official update notification not delivered immediately");
        }
        using(var form=new BatteryForm()) {
            if(!form.TopMost)throw new Exception("Widget is not topmost");
            form.Location=new Point(120,120);form.BeginDrag(new Point(140,140));form.ContinueDrag(new Point(205,185));form.EndDrag();
            if(form.Left!=185||form.Top!=165)throw new Exception("Pointer drag did not move widget");
            var saved=Quota.Obj(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(Data,"position.json"))));
            if(Convert.ToInt32(saved["x"])!=185||Convert.ToInt32(saved["y"])!=165)throw new Exception("Dragged position was not persisted");
        }
        File.WriteAllText(Path.Combine(Data,"selftest.json"),"{\"ok\":true,\"tests\":[\"remaining-not-used\",\"clamping\",\"continuous-colors\",\"window-duration-mapping\",\"unknown-is-not-full\",\"immediate-official-notification\",\"topmost-widget\",\"drag-and-position-persistence\"]}");
    }
}

sealed class Quota {
    public double? ShortRemaining, WeekRemaining;
    public long? ShortReset, WeekReset;
    public DateTime UpdatedUtc;
    public static double Remaining(double used){return Math.Max(0,Math.Min(100,100-used));}
    internal static Dictionary<string,object> Obj(object value){return value as Dictionary<string,object> ?? new Dictionary<string,object>();}
    internal static object Get(Dictionary<string,object> d,string key){object v;return d.TryGetValue(key,out v)?v:null;}
    public static Quota Parse(string json) {
        var data=Obj(new JavaScriptSerializer().DeserializeObject(json));
        var buckets=Obj(Get(data,"rateLimitsByLimitId"));
        var bucket=Obj(Get(buckets,"codex"));
        if(bucket.Count==0)bucket=Obj(Get(data,"rateLimits"));
        var q=new Quota{UpdatedUtc=DateTime.UtcNow};
        foreach(string key in new[]{"primary","secondary"}) {
            var w=Obj(Get(bucket,key));
            if(Get(w,"usedPercent")==null || Get(w,"windowDurationMins")==null)continue;
            double minutes=Convert.ToDouble(w["windowDurationMins"]),left=Remaining(Convert.ToDouble(w["usedPercent"]));
            long? reset=Get(w,"resetsAt")==null?(long?)null:Convert.ToInt64(w["resetsAt"]);
            if(minutes==300){q.ShortRemaining=left;q.ShortReset=reset;}
            if(minutes==10080){q.WeekRemaining=left;q.WeekReset=reset;}
        }
        return q;
    }
    public void Save() {
        // Only public-to-the-user percentages and reset times; no account ID or credentials.
        string text=new JavaScriptSerializer().Serialize(new { shortRemaining=ShortRemaining, weekRemaining=WeekRemaining, shortReset=ShortReset, weekReset=WeekReset, updatedUtc=UpdatedUtc.ToString("o") });
        string dest=Path.Combine(Program.Data,"status.json"),temp=dest+".tmp";
        File.WriteAllText(temp,text,Encoding.UTF8);
        if(File.Exists(dest))File.Replace(temp,dest,null);else File.Move(temp,dest);
    }
}

sealed class QuotaRpc : IDisposable {
    Process process;
    StreamWriter input;
    BlockingCollection<Dictionary<string,object>> incoming;
    int sequence;
    public event Action<Quota> Updated;
    public string TransportName = "connecting";
    internal static string FindCodex() {
        var config=Path.Combine(Program.Root,"settings.json");
        if(File.Exists(config)) {
            var data=Quota.Obj(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(config)));
            var value=Quota.Get(data,"codexPath") as string;
            if(value!=null && File.Exists(value))return value;
        }
        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        if(Directory.Exists(folder)) {
            var found=Directory.GetFiles(folder,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if(found!=null)return found;
        }
        throw new Exception("未找到官方 Codex CLI，请在 settings.json 配置 codexPath");
    }
    void Open() {
        try { OpenTransport("app-server proxy", "shared-notifications"); }
        catch { Dispose(); OpenTransport("app-server --stdio", "direct-read"); }
    }
    void OpenTransport(string arguments, string name) {
        incoming=new BlockingCollection<Dictionary<string,object>>();
        process=new Process{StartInfo=new ProcessStartInfo(FindCodex(),arguments) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8}};
        process.Start();
        input=new StreamWriter(process.StandardInput.BaseStream,new UTF8Encoding(false));input.AutoFlush=true;
        process.ErrorDataReceived+=(s,e)=>{};process.BeginErrorReadLine();
        var source=process;var messages=incoming;
        var thread=new Thread(()=>{
            try { string line;while((line=source.StandardOutput.ReadLine())!=null) {
                try {
                    var message=Quota.Obj(new JavaScriptSerializer().DeserializeObject(line));
                    if(!Object.ReferenceEquals(process,source))continue;
                    if(!HandleNotification(message))messages.Add(message);
                }catch(ArgumentException){}catch(InvalidOperationException){}
            }}catch(IOException){}catch(ObjectDisposedException){}
        });thread.IsBackground=true;thread.Start();
        Request("initialize",new {clientInfo=new {name="codex_quota_battery",title="Quota Battery",version="2.2.0"}},3);
        input.WriteLine("{\"method\":\"initialized\",\"params\":{}}");
        TransportName=name;
    }
    internal bool HandleNotification(Dictionary<string,object> message) {
        if(Convert.ToString(Quota.Get(message,"method"))!="account/rateLimits/updated")return false;
        var q=Quota.Parse(new JavaScriptSerializer().Serialize(Quota.Get(message,"params")));
        var handler=Updated;if(handler!=null)handler(q);return true;
    }
    string Request(string method,object parameters,int timeoutSeconds=15) {
        int id=++sequence;
        input.WriteLine(new JavaScriptSerializer().Serialize(new {id=id,method=method,@params=parameters}));
        var deadline=DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while(DateTime.UtcNow<deadline) {
            Dictionary<string,object> response;
            if(!incoming.TryTake(out response,1000)) {if(process.HasExited)throw new IOException("Codex 额度连接已退出");continue;}
            if(Convert.ToString(Quota.Get(response,"id"))!=id.ToString())continue;
            if(Quota.Get(response,"error")!=null)throw new IOException("Codex 额度接口暂不可用");
            return new JavaScriptSerializer().Serialize(Quota.Get(response,"result"));
        }
        throw new TimeoutException("额度刷新超时");
    }
    public Quota Read() {if(process==null||process.HasExited)Open();return Quota.Parse(Request("account/rateLimits/read",new {}));}
    public void Dispose() {try{if(process!=null&&!process.HasExited)process.Kill();}catch{}if(process!=null)process.Dispose();process=null;}
}

sealed class BatteryForm : Form {
    readonly ManualResetEvent stop=new ManualResetEvent(false), refresh=new ManualResetEvent(false);
    readonly ToolTip tooltip=new ToolTip();
    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer animation=new System.Windows.Forms.Timer();
    readonly Stopwatch animationClock=Stopwatch.StartNew();
    double shownShort=Double.NaN,shownWeek=Double.NaN;
    double? previewSeconds;
    bool effects=true;
    Quota current;
    string error="正在读取额度…";
    bool closing;
    QuotaRpc activeRpc;
    bool dragging, moved;
    Point dragStart, windowStart;
    string transport="connecting";
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80|0x80000;return p;}}
    public BatteryForm() {
        SetProcessDPIAware();Text="Codex 额度电池条";Name="CodexQuotaBattery";
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
        BackColor=Color.FromArgb(28,29,33);ForeColor=Color.White;Opacity=1;ClientSize=new Size(70,88);TopMost=true;
        DoubleBuffered=true;Font=new Font("Microsoft YaHei UI",9);

        var menu=new ContextMenuStrip();
        var recover=new ToolStripMenuItem("额度恢复后继续中断任务");
        recover.Checked=RecoveryEnabled();
        recover.Click+=(s,e)=>{recover.Checked=!recover.Checked;File.WriteAllText(Path.Combine(Program.Data,"recovery-settings.json"),new JavaScriptSerializer().Serialize(new{enabled=recover.Checked,generation=Guid.NewGuid().ToString()}));};
        menu.Items.Add(recover);
        menu.Items.Add("查看自动续跑状态",null,(s,e)=>{
            try {
                var d=Quota.Obj(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(Program.Data,"recovery-status.json"))));
                MessageBox.Show("状态："+Convert.ToString(Quota.Get(d,"status"))+"\n监测任务："+Convert.ToString(Quota.Get(d,"watchedCount"))+"\n等待额度恢复："+Convert.ToString(Quota.Get(d,"waitingCount"))+"\n已续跑："+Convert.ToString(Quota.Get(d,"resumedCount"))+"\n最后检查："+Convert.ToString(Quota.Get(d,"updatedUtc"))+"\n"+Convert.ToString(Quota.Get(d,"reason")),"自动续跑状态");
            }catch {MessageBox.Show("尚未取得监控状态，请确认 Codex 桌面连接正常。","自动续跑状态");}
        });
        var motion=menu.Items.Add("动态特效");
        ((ToolStripMenuItem)motion).Checked=effects;
        motion.Click+=(s,e)=>{effects=!effects;((ToolStripMenuItem)motion).Checked=effects;Invalidate();};
        animation.Interval=40;
        animation.Tick+=(s,e)=>{if(current!=null){shownShort=Ease(shownShort,current.ShortRemaining);shownWeek=Ease(shownWeek,current.WeekRemaining);}if(Visible)RenderLayer();};
        menu.Items.Add("立即刷新",null,(s,e)=>refresh.Set());
        menu.Items.Add("打开使用说明",null,(s,e)=>Process.Start(new ProcessStartInfo(Path.Combine(Program.Root,"README.md")){UseShellExecute=true}));
        menu.Items.Add("退出电池条",null,(s,e)=>Close());ContextMenuStrip=menu;
        tray=new NotifyIcon{Icon=SystemIcons.Information,Text="Codex 额度电池条 · 可拖动 · 2秒核对",Visible=true,ContextMenuStrip=menu};
        tray.DoubleClick+=(s,e)=>refresh.Set();
        LoadPosition();
        MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left)BeginDrag(Cursor.Position);};
        MouseMove+=(s,e)=>ContinueDrag(Cursor.Position);
        MouseUp+=(s,e)=>{if(e.Button==MouseButtons.Left)EndDrag();};
        Shown+=(s,e)=>{animation.Start();WriteWidgetState();StartRecovery();var worker=new Thread(Poll);worker.IsBackground=true;worker.Start();};
    }
    bool RecoveryEnabled() {
        try {var path=Path.Combine(Program.Data,"recovery-settings.json");if(File.Exists(path))return Convert.ToBoolean(Quota.Get(Quota.Obj(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path))),"enabled"));}catch{}
        return true;
    }
    void StartRecovery() {
        try {
            var cfg=Quota.Obj(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(Program.Root,"recovery-runtime.json"))));
            Process.Start(new ProcessStartInfo(Convert.ToString(cfg["python"]),"\""+Path.Combine(Program.Root,"recovery_monitor.py")+"\" --parent "+Process.GetCurrentProcess().Id){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Program.Root});
        }catch {File.WriteAllText(Path.Combine(Program.Data,"recovery-status.json"),"{\"status\":\"startup_error\"}");}
    }
    internal void BeginDrag(Point pointer) {dragging=true;moved=false;dragStart=pointer;windowStart=Location;Capture=true;}
    internal void ContinueDrag(Point pointer) {if(!dragging)return;int dx=pointer.X-dragStart.X,dy=pointer.Y-dragStart.Y;if(Math.Abs(dx)+Math.Abs(dy)>3)moved=true;if(moved)Location=ClampPosition(new Point(windowStart.X+dx,windowStart.Y+dy));}
    internal void EndDrag() {dragging=false;Capture=false;if(moved)SavePosition();else refresh.Set();}
    Point ClampPosition(Point p) {
        var nearest=Screen.FromPoint(new Point(p.X+Width/2,p.Y+Height/2)).WorkingArea;
        return new Point(Math.Max(nearest.Left,Math.Min(p.X,nearest.Right-Width)),Math.Max(nearest.Top,Math.Min(p.Y,nearest.Bottom-Height)));
    }
    void LoadPosition() {
        var area=Screen.PrimaryScreen.WorkingArea;var p=new Point(area.Right-Width-28,area.Bottom-Height-50);
        try{var file=Path.Combine(Program.Data,"position.json");if(File.Exists(file)){var d=Quota.Obj(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file)));p=new Point(Convert.ToInt32(d["x"]),Convert.ToInt32(d["y"]));}}catch{}
        Location=ClampPosition(p);
    }
    void SavePosition() {
        try{File.WriteAllText(Path.Combine(Program.Data,"position.json"),new JavaScriptSerializer().Serialize(new{x=Left,y=Top}));WriteWidgetState();}catch{}
    }
    void WriteWidgetState() {
        try{File.WriteAllText(Path.Combine(Program.Data,"widget-state.json"),new JavaScriptSerializer().Serialize(new{pid=Process.GetCurrentProcess().Id,visible=Visible,topMost=TopMost,draggable=true,x=Left,y=Top,transport=transport,reconcileMs=2000,updatedUtc=DateTime.UtcNow.ToString("o")}));}catch{}
    }
    void Publish(Quota q) {
        if(closing)return;
        try{BeginInvoke((Action)(()=>{if(closing)return;current=q;error="";try{q.Save();}catch(IOException){}catch(UnauthorizedAccessException){}UpdateTip();WriteWidgetState();RenderLayer();}));}catch(InvalidOperationException){}
    }
    void Poll() {
        QuotaRpc rpc=null;
        while(!stop.WaitOne(0)) {
            var cycle=Stopwatch.StartNew();
            bool succeeded=false;
            try {
                if(rpc==null){rpc=new QuotaRpc();rpc.Updated+=Publish;}
                activeRpc=rpc;
                var q=rpc.Read();transport=rpc.TransportName;Publish(q);succeeded=true;
            }catch(Exception) {
                if(rpc!=null)rpc.Dispose();rpc=null;
                if(!closing)try{BeginInvoke((Action)(()=>{error="刷新失败，稍后重试";UpdateTip();Invalidate();}));}catch(InvalidOperationException){}
            }
            int delay=succeeded?Math.Max(100,2000-(int)cycle.ElapsedMilliseconds):10000;
            int result=WaitHandle.WaitAny(new WaitHandle[]{stop,refresh},delay);refresh.Reset();if(result==0)break;
        }
        if(rpc!=null)rpc.Dispose();
    }
    static string ResetTime(long? stamp) {
        if(!stamp.HasValue)return "未知";
        return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(stamp.Value).ToLocalTime().ToString("MM-dd HH:mm");
    }
    void UpdateTip() {
        string text=current==null?error:"左 · 五小时剩余："+(current.ShortRemaining.HasValue?current.ShortRemaining.Value.ToString("0")+"%":"未知")+"\n右 · 一周剩余："+(current.WeekRemaining.HasValue?current.WeekRemaining.Value.ToString("0")+"%":"未知")+"\n"+"五小时重置："+ResetTime(current.ShortReset)+"\n一周重置："+ResetTime(current.WeekReset)+"\n更新于："+current.UpdatedUtc.ToLocalTime().ToString("HH:mm:ss")+"\n"+(error==""?(transport=="shared-notifications"?"官方通知即更新 · 每2秒核对":"官方只读接口 · 每2秒核对")+"\n按住拖动 · 点击刷新 · 右键退出":error);
        tooltip.SetToolTip(this,text);
    }
    internal static Color BarColor(double remaining) {
        // Continuous red -> amber -> green, driven by remaining rather than used quota.
        double t=Math.Max(0,Math.Min(1,remaining/100));
        if(t<.5)return Color.FromArgb(235,(int)(70+t*2*110),65);
        return Color.FromArgb((int)(235-(t-.5)*2*170),180+(int)((t-.5)*2*15),85);
    }
    static GraphicsPath Rounded(Rectangle rect,int radius) {
        var p=new GraphicsPath();int d=radius*2;
        p.AddArc(rect.X,rect.Y,d,d,180,90);p.AddArc(rect.Right-d,rect.Y,d,d,270,90);p.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90);p.AddArc(rect.X,rect.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    internal static int LitSegments(double value) {return value<=0?0:Math.Min(10,(int)Math.Ceiling(value/10));}
    double Ease(double shown,double? target) {
        if(!target.HasValue)return Double.NaN;
        if(Double.IsNaN(shown)||!effects||Math.Abs(shown-target.Value)<.05)return target.Value;
        return shown+(target.Value-shown)*.18;
    }
    void CenterText(Graphics g,string text,Font font,Color color,Rectangle area) {
        using(var brush=new SolidBrush(color))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(text,font,brush,area,format);
    }
    [StructLayout(LayoutKind.Sequential)] struct Blend {public byte Operation,Flags,Alpha,Format;}
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr dst,ref Point location,ref Size size,IntPtr src,ref Point origin,int key,ref Blend blend,int flags);
    void DrawBattery(Graphics g,double? remaining,double shown,int x) {
        double level=Math.Max(0,Math.Min(100,Double.IsNaN(shown)?(remaining??0):shown));
        double phase=effects?(previewSeconds??animationClock.Elapsed.TotalSeconds):0;
        Color color=BarColor(level);
        var body=new Rectangle(x,12,25,69);
        using(var cap=Rounded(new Rectangle(x+8,7,9,5),2))using(var brush=new LinearGradientBrush(new Rectangle(x+8,7,9,5),Color.FromArgb(150,225,234,245),Color.FromArgb(65,90,105,125),0f))g.FillPath(brush,cap);
        using(var shell=Rounded(body,7)) {
            using(var glass=new LinearGradientBrush(body,Color.FromArgb(55,215,230,246),Color.FromArgb(28,40,57,79),0f))g.FillPath(glass,shell);
            using(var edge=new Pen(Color.FromArgb(145,190,205,224),1))g.DrawPath(edge,shell);
        }
        var inside=new Rectangle(x+3,15,19,63);
        using(var clip=Rounded(inside,5)) {
            var state=g.Save();g.SetClip(clip);
            if(remaining.HasValue&&level>0) {
                float y=inside.Bottom-(float)(inside.Height*level/100);
                using(var liquid=new GraphicsPath()) {
                    for(int i=0;i<=24;i++) {
                        float px=x+1+i;
                        float py=y+(float)(Math.Sin(i*.24+phase*2.4)*1.35+Math.Sin(i*.43-phase*1.8)*.5);
                        if(i==0)liquid.StartFigure();else liquid.AddLine(px-1,y+(float)(Math.Sin((i-1)*.24+phase*2.4)*1.35+Math.Sin((i-1)*.43-phase*1.8)*.5),px,py);
                    }
                    liquid.AddLine(x+25,inside.Bottom+2,x,inside.Bottom+2);liquid.CloseFigure();
                    using(var fill=new LinearGradientBrush(inside,Color.FromArgb(135,color),Color.FromArgb(95,color),0f))g.FillPath(fill,liquid);
                    using(var surface=new Pen(Color.FromArgb(190,color),.8f)) {
                        for(int i=1;i<=24;i++)g.DrawLine(surface,x+i,y+(float)(Math.Sin((i-1)*.24+phase*2.4)*1.35+Math.Sin((i-1)*.43-phase*1.8)*.5),x+1+i,y+(float)(Math.Sin(i*.24+phase*2.4)*1.35+Math.Sin(i*.43-phase*1.8)*.5));
                    }
                }
            }
            using(var highlight=new LinearGradientBrush(inside,Color.FromArgb(105,255,255,255),Color.FromArgb(0,255,255,255),0f))g.FillRectangle(highlight,x+3,16,7,60);
            g.Restore(state);
        }
        using(var shine=new Pen(Color.FromArgb(175,255,255,255),1))g.DrawLine(shine,x+4,21,x+4,48);
        using(var shade=new Pen(Color.FromArgb(65,20,35,55),1))g.DrawLine(shade,x+22,23,x+22,71);
    }
    void DrawContent(Graphics g) {
        g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
        DrawBattery(g,current==null?null:current.ShortRemaining,shownShort,5);
        DrawBattery(g,current==null?null:current.WeekRemaining,shownWeek,40);
    }
    void RenderLayer() {
        if(!IsHandleCreated||closing)return;
        using(var bitmap=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb)) {
            using(var g=Graphics.FromImage(bitmap))DrawContent(g);
            IntPtr screen=GetDC(IntPtr.Zero),memory=CreateCompatibleDC(screen),image=bitmap.GetHbitmap(Color.FromArgb(0));
            IntPtr previous=SelectObject(memory,image);
            try {Point position=Location,origin=Point.Empty;Size size=Size;Blend blend=new Blend{Alpha=255,Format=1};UpdateLayeredWindow(Handle,screen,ref position,ref size,memory,ref origin,0,ref blend,2);}
            finally {SelectObject(memory,previous);DeleteObject(image);DeleteDC(memory);ReleaseDC(IntPtr.Zero,screen);}
        }
    }
    protected override void OnPaint(PaintEventArgs e) {RenderLayer();}
    internal void Preview(string path) {
        using(var rpc=new QuotaRpc())current=rpc.Read();error="";
        using(var bitmap=new Bitmap(Width,Height)){using(var g=Graphics.FromImage(bitmap))DrawContent(g);bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}
    }
    internal void PreviewDemo(string directory) {
        Directory.CreateDirectory(directory);
        current=new Quota{ShortRemaining=30,WeekRemaining=77,UpdatedUtc=DateTime.UtcNow};
        for(int i=0;i<96;i++) {
            previewSeconds=i/24.0;
            using(var bitmap=new Bitmap(Width,Height)){using(var g=Graphics.FromImage(bitmap))DrawContent(g);bitmap.Save(Path.Combine(directory,"battery-"+i.ToString("000")+".png"),System.Drawing.Imaging.ImageFormat.Png);}
        }
    }
    protected override void OnFormClosed(FormClosedEventArgs e) {closing=true;animation.Stop();animation.Dispose();tooltip.Dispose();stop.Set();if(activeRpc!=null)activeRpc.Dispose();tray.Dispose();base.OnFormClosed(e);}
}


