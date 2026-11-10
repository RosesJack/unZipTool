using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace PrecisionExtract {
 static class Program {
  [STAThread] static int Main(string[] args) {
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   try {
    if(args.Contains("--headless")) {
     var s=new Settings();s.Rules.Clear();string archive=null,report=null;
     for(int i=0;i<args.Length;i++) {
      switch(args[i]) {case "--headless":archive=args[++i];break;case "--output":s.Output=args[++i];break;case "--rule":s.Rules.Add(args[++i]);break;case "--literal":s.Regex=false;break;case "--report":report=args[++i];break;}
     }
     var r=new Extractor(s,x=>{},CancellationToken.None).Run(archive);
     if(report!=null)using(var f=File.Create(report))new XmlSerializer(typeof(ExtractResult)).Serialize(f,r);
     return r.Warnings.Count>0?2:r.Files.Count==0?3:0;
    }
    Settings config=Settings.Load();
    if(args.Length>=2 && args[0]=="--context")using(var context=new TaskContext(config,Path.GetFullPath(args[1])))Application.Run(context);
    else Application.Run(new MainWindow(config,args.Length>0?args[0]:null));
    return 0;
   } catch(Exception e) { if(!args.Contains("--headless")){if(args.Contains("--context")){using(var context=new ApplicationContext())using(var timer=new System.Windows.Forms.Timer{Interval=15000}){timer.Tick+=(a,b)=>context.ExitThread();timer.Start();Notifications.Show("解压失败",e.Message,ToolTipIcon.Error);Application.Run(context);}}else MessageBox.Show(e.Message,"精准解压",MessageBoxButtons.OK,MessageBoxIcon.Error);}return 1; }
  }
  public static string FindEditor(string configured) {
   if(!string.IsNullOrWhiteSpace(configured)&&File.Exists(configured))return configured;
   foreach(var env in new[]{Environment.SpecialFolder.ProgramFiles,Environment.SpecialFolder.ProgramFilesX86}) {
    string p=Path.Combine(Environment.GetFolderPath(env),"Notepad++","notepad++.exe");if(File.Exists(p))return p;
   }
   using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\notepad++.exe")) {if(key!=null) {string p=key.GetValue("") as string;if(File.Exists(p))return p;}}
   throw new IOException("未找到 Notepad++。请在“设置”中指定 notepad++.exe，或取消自动打开选项。");
  }
  public static void OpenResult(Settings s,ExtractResult r,Action<string> openDirectory=null,Action<Settings,List<string>> openEditor=null) {
   if(r.Files.Count==0||r.Cancelled||r.Warnings.Count>0)return;
   if(s.OpenDirectory){if(openDirectory!=null)openDirectory(r.Output);else Process.Start(new ProcessStartInfo("explorer.exe","\""+r.Output+"\""){UseShellExecute=true});}
   if(s.OpenEditor) {
    if(openEditor!=null){openEditor(s,r.Files);return;}
    LaunchEditor(FindEditor(s.Editor),r.Files);
   }
  }
  public static void LaunchEditor(string editor,IEnumerable<string> files,Action<string,string> launch=null) {
   if(launch==null)launch=(exe,args)=>{using(var process=Process.Start(exe,args)){};};
   const string options="-multiInst -nosession ";
   var batch=new List<string>();int size=options.Length;
   foreach(string path in files) {
    if(batch.Count>0 && size+path.Length+3>20000){launch(editor,options+string.Join(" ",batch));batch.Clear();size=options.Length;}
    batch.Add("\""+path+"\"");size+=path.Length+3;
   }
   if(batch.Count>0)launch(editor,options+string.Join(" ",batch));
  }
 }
 // Keep the notification icon alive briefly after a context task closes its window.
 // No hidden main window and no permanent background process are needed.
 class TaskContext:ApplicationContext {
  readonly ProgressWindow window;
  readonly System.Windows.Forms.Timer lifetime=new System.Windows.Forms.Timer{Interval=15000};
  public TaskContext(Settings settings,string archive){
   window=new ProgressWindow(settings,archive);
   window.FormClosed+=(a,b)=>lifetime.Start();
   lifetime.Tick+=(a,b)=>{lifetime.Stop();ExitThread();};
   window.Show();
  }
  protected override void Dispose(bool disposing){if(disposing){lifetime.Dispose();window.Dispose();}base.Dispose(disposing);}
 }
 static class Notifications {
  static NotifyIcon icon;
  static Notifications(){Application.ApplicationExit+=(a,b)=>{if(icon!=null){icon.Visible=false;icon.Dispose();icon=null;clickHandler=null;}};}
  public static void Show(string title,string text,ToolTipIcon kind,Action clicked=null) {
   if(icon==null){icon=new NotifyIcon{Icon=SystemIcons.Application,Text="精准解压",Visible=true};}
   icon.BalloonTipTitle=title.Length>63?title.Substring(0,60)+"…":title;
   icon.BalloonTipText=string.IsNullOrWhiteSpace(text)?"任务结束":text.Length>240?text.Substring(0,237)+"…":text;
   icon.BalloonTipIcon=kind;
   if(clickHandler!=null)icon.BalloonTipClicked-=clickHandler;
   clickHandler=(a,b)=>{if(clicked!=null)clicked();};icon.BalloonTipClicked+=clickHandler;
   icon.ShowBalloonTip(8000);
  }
  static EventHandler clickHandler;
 }
 class DpiForm:Form {
  public DpiForm(){SuspendLayout();AutoScaleDimensions=new SizeF(96F,96F);AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("Microsoft YaHei UI",10F);}
  protected override void OnLoad(EventArgs e){ResumeLayout(true);base.OnLoad(e);}
 }
 static class ShellMenu {
  static readonly string[] Extensions={".zip",".tar",".rar",".7z",".gz",".gzip",".tgz",".bz2",".tbz2",".xz",".txz",".zst",".zstd"};
  static string KeyPath(string ext) {return @"Software\Classes\SystemFileAssociations\"+ext+@"\shell\PrecisionExtract";}
  public static bool Enabled {get {using(var k=Registry.CurrentUser.OpenSubKey(KeyPath(".zip")))return k!=null;}}
  public static void Set(bool enabled) {
   foreach(string ext in Extensions) {
    if(!enabled) {Registry.CurrentUser.DeleteSubKeyTree(KeyPath(ext),false);continue;}
    using(var k=Registry.CurrentUser.CreateSubKey(KeyPath(ext))) {
     k.SetValue("","精准解压（按已保存规则）");k.SetValue("Icon",Application.ExecutablePath);k.SetValue("MultiSelectModel","Document");
     using(var c=k.CreateSubKey("command"))c.SetValue("","\""+Application.ExecutablePath+"\" --context \"%1\"");
    }
   }
  }
 }
 class MainWindow:DpiForm {
  Settings settings;TextBox archive=new TextBox(),output=new TextBox();ListBox rules=new ListBox();CheckBox regex=new CheckBox(),editor=new CheckBox(),openDirectory=new CheckBox();Button delete;Label status=new Label();
  public MainWindow(Settings s,string initial) {
   settings=s;Text="精准解压";Font=new Font("Microsoft YaHei UI",10F);Size=new Size(760,670);MinimumSize=new Size(570,590);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.White;
   var menu=new MenuStrip{Font=Font};var file=new ToolStripMenuItem("文件");file.DropDownItems.Add("选择压缩文件…",null,(a,b)=>Choose());file.DropDownItems.Add("退出",null,(a,b)=>Close());menu.Items.Add(file);menu.Items.Add("设置",null,(a,b)=>Configure());menu.Items.Add("帮助",null,(a,b)=>MessageBox.Show("选择或拖入压缩包，设置文件名规则后开始解压。\nCtrl 多选，Shift 连选，双击编辑规则。\n输出：根目录\\日期\\问题N；同名文件加后缀。\n右键菜单使用已保存设置，多个任务可以同时运行。\nWindows 11 中可能位于“显示更多选项”。","使用说明"));MainMenuStrip=menu;
   var panel=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(22,14,22,12),ColumnCount=1,RowCount=13};
   panel.RowStyles.Add(new RowStyle(SizeType.Absolute,28));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,44));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,30));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
   foreach(int height in new[]{42,24,28,44,34,32,32,45,26})panel.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
   panel.Controls.Add(Label("压缩文件   ZIP · TAR · RAR · 7Z · GZ"),0,0);
   archive.ReadOnly=true;archive.Text=initial??"";panel.Controls.Add(InputRow(archive,"选择文件…",Choose),0,1);
   var heading=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));heading.Controls.Add(Label("要提取的文件"));regex.Text="使用正则表达式";regex.Checked=s.Regex;regex.AutoSize=true;heading.Controls.Add(regex);panel.Controls.Add(heading,0,2);
   rules.SelectionMode=SelectionMode.MultiExtended;rules.Dock=DockStyle.Fill;rules.IntegralHeight=false;rules.Font=new Font("Consolas",11);rules.Items.AddRange(s.Rules.Cast<object>().ToArray());rules.DoubleClick+=(a,b)=>EditRule();rules.KeyDown+=(a,b)=>{if(b.KeyCode==Keys.Delete)DeleteRules();if(b.KeyCode==Keys.F2)EditRule();};panel.Controls.Add(rules,0,3);
   var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,5,0,0)};actions.Controls.Add(Button("＋ 添加规则",()=>RuleDialog(-1)));delete=Button("删除",DeleteRules);delete.Enabled=false;actions.Controls.Add(delete);actions.Controls.Add(Button("测试规则",TestRules));rules.SelectedIndexChanged+=(a,b)=>delete.Enabled=rules.SelectedIndices.Count>0;panel.Controls.Add(actions,0,4);
   panel.Controls.Add(Label("Ctrl 多选 · Shift 连选 · 双击编辑 · 任一规则命中即提取",true),0,5);panel.Controls.Add(Label("保存到"),0,6);output.Text=s.Output;panel.Controls.Add(InputRow(output,"浏览…",()=>{using(var d=new FolderBrowserDialog()){d.Description="选择输出根目录；留空默认使用 文档\\精准解压";if(d.ShowDialog()==DialogResult.OK)output.Text=d.SelectedPath;}}),0,7);
   panel.Controls.Add(Label("自动递归嵌套压缩包 · 任务结束清理临时文件",true),0,8);editor.Text="完成后用 Notepad++ 打开全部命中文件";editor.Checked=s.OpenEditor;editor.AutoSize=true;panel.Controls.Add(editor,0,9);
   openDirectory.Text="完成后自动打开结果目录";openDirectory.Checked=s.OpenDirectory;openDirectory.AutoSize=true;panel.Controls.Add(openDirectory,0,10);
   var run=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};run.Controls.Add(Button("开始解压",Run));run.Controls.Add(Button("保存配置",()=>{try{CaptureSettings().Save();status.Text="配置已保存";}catch(Exception e){Error(e);}}));panel.Controls.Add(run,0,11);status.Text="就绪 · 任务结果通过 Windows 通知提醒";status.Dock=DockStyle.Fill;status.ForeColor=Color.DimGray;panel.Controls.Add(status,0,12);
   Controls.Add(panel);Controls.Add(menu);AllowDrop=true;DragEnter+=(a,b)=>{if(b.Data.GetDataPresent(DataFormats.FileDrop))b.Effect=DragDropEffects.Copy;};DragDrop+=(a,b)=>{var paths=(string[])b.Data.GetData(DataFormats.FileDrop);if(paths.Length>0)archive.Text=paths[0];};
  }
  static Label Label(string text,bool muted=false) {return new Label{Text=text,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=muted?Color.DimGray:Color.Black,AutoEllipsis=true};}
  public static Button Button(string text,Action action) {var b=new Button{Text=text,AutoSize=true,MinimumSize=new Size(82,29),FlatStyle=FlatStyle.System};b.Click+=(a,e)=>action();return b;}
  static Control InputRow(TextBox input,string text,Action action) {var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));input.Dock=DockStyle.Top;input.Margin=new Padding(0,4,8,0);row.Controls.Add(input);row.Controls.Add(Button(text,action));return row;}
  void Error(Exception e){MessageBox.Show(this,e.Message,"精准解压",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
  void Choose(){using(var d=new OpenFileDialog{Filter="压缩文件|*.zip;*.tar;*.rar;*.7z;*.gz;*.gzip;*.tgz;*.bz2;*.tbz2;*.xz;*.txz;*.zst;*.zstd|所有文件|*.*"})if(d.ShowDialog()==DialogResult.OK)archive.Text=d.FileName;}
  Settings CaptureSettings(){return new Settings{Output=output.Text.Trim(),Regex=regex.Checked,OpenEditor=editor.Checked,OpenDirectory=openDirectory.Checked,Editor=settings.Editor,Rules=rules.Items.Cast<string>().ToList()};}
  void DeleteRules(){foreach(int i in rules.SelectedIndices.Cast<int>().OrderByDescending(i=>i).ToArray())rules.Items.RemoveAt(i);}
  void EditRule(){if(rules.SelectedIndices.Count==1)RuleDialog(rules.SelectedIndex);}
  void RuleDialog(int index) {
   using(var d=new DpiForm{Text=index<0?"添加规则":"编辑规则",Size=new Size(470,170),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false,Font=Font}) {
    var input=new TextBox{Left=18,Top=20,Width=415,Text=index<0?"":rules.Items[index].ToString()};var save=Button(index<0?"添加":"保存",()=>{try{if(string.IsNullOrWhiteSpace(input.Text))throw new ArgumentException("请输入规则。");if(regex.Checked)new System.Text.RegularExpressions.Regex(input.Text);if(index<0){rules.SelectedIndex=rules.Items.Add(input.Text);}else rules.Items[index]=input.Text;d.DialogResult=DialogResult.OK;}catch(Exception e){Error(e);}});save.Location=new Point(260,65);var cancel=Button("取消",()=>d.Close());cancel.Location=new Point(350,65);d.Controls.AddRange(new Control[]{input,save,cancel});d.AcceptButton=save;d.CancelButton=cancel;d.ShowDialog(this);
   }
  }
  void TestRules(){try{new Extractor(CaptureSettings(),x=>{},CancellationToken.None);MessageBox.Show(this,"规则格式有效，共 "+rules.Items.Count+" 条。","测试规则");}catch(Exception e){Error(e);}}
  void Run(){try{var config=CaptureSettings();new Extractor(config,x=>{},CancellationToken.None);if(!File.Exists(archive.Text))throw new IOException("请选择存在的压缩文件。");if(config.OpenEditor)Program.FindEditor(config.Editor);config.Save();settings=config;using(var progress=new ProgressWindow(config,archive.Text))progress.ShowDialog(this);}catch(Exception e){Error(e);}}
  void Configure(){
   using(var d=new DpiForm{Text="设置",Size=new Size(560,275),StartPosition=FormStartPosition.CenterParent,Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false}) {
    var label=new Label{Text="Notepad++ 程序路径（留空自动检测）",Left=18,Top=16,Width=480};var path=new TextBox{Left=18,Top=45,Width=405,Text=settings.Editor};var browse=Button("浏览…",()=>{using(var f=new OpenFileDialog{Filter="Notepad++|notepad++.exe"})if(f.ShowDialog()==DialogResult.OK)path.Text=f.FileName;});browse.Location=new Point(432,42);
    var shell=new CheckBox{Text="在压缩文件右键菜单中添加“精准解压”",Left=18,Top=90,Width=490,Checked=ShellMenu.Enabled};var hint=new Label{Text="使用当前用户菜单，无需管理员权限。\n移动程序后需重新添加菜单；删除程序前请先取消勾选。",Left=18,Top=122,Width=490,Height=45,ForeColor=Color.DimGray};var save=Button("保存",()=>{try{settings.Editor=path.Text.Trim();var current=CaptureSettings();current.Editor=settings.Editor;current.Save();ShellMenu.Set(shell.Checked);d.Close();}catch(Exception e){Error(e);}});save.Location=new Point(430,180);d.Controls.AddRange(new Control[]{label,path,browse,shell,hint,save});d.ShowDialog(this);
   }
  }
 }
 class ProgressWindow:DpiForm {
  readonly Settings settings;readonly string archive;readonly CancellationTokenSource cancellation=new CancellationTokenSource();TextBox detail=new TextBox{Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=SystemColors.Control,ScrollBars=ScrollBars.Vertical};Label summary=new Label();ProgressBar progress=new ProgressBar();Button cancel;bool running=true;ExtractResult result;
  public ProgressWindow(Settings s,string file) {
   settings=s;archive=file;Text="精准解压";Size=new Size(500,270);MinimumSize=new Size(430,270);MaximumSize=new Size(800,320);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10F);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;
   var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=5};foreach(int h in new[]{32,30,50,22,40})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));summary.Text="正在解压…";summary.Font=new Font(Font,FontStyle.Bold);summary.Dock=DockStyle.Fill;detail.Dock=DockStyle.Fill;detail.Text="正在准备…";var filename=new Label{Text=Path.GetFileName(file),Dock=DockStyle.Fill,AutoEllipsis=true,ForeColor=Color.DimGray};progress.Style=ProgressBarStyle.Marquee;progress.MarqueeAnimationSpeed=30;progress.Dock=DockStyle.Fill;var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};cancel=MainWindow.Button("取消",()=>{if(running){cancel.Enabled=false;cancellation.Cancel();detail.Text="正在取消并清理临时文件…";}else Close();});actions.Controls.Add(cancel);layout.Controls.Add(summary,0,0);layout.Controls.Add(filename,0,1);layout.Controls.Add(detail,0,2);layout.Controls.Add(progress,0,3);layout.Controls.Add(actions,0,4);Controls.Add(layout);
   FormClosing+=(a,e)=>{if(running){e.Cancel=true;cancellation.Cancel();cancel.Enabled=false;detail.Text="正在取消并清理临时文件…";}};
   Shown+=async (a,e)=>{
    try {
     var engine=new Extractor(settings,text=>{if(!IsDisposed)BeginInvoke(new Action(()=>detail.Text=text));},cancellation.Token);
     result=await Task.Run(()=>engine.Run(archive));running=false;Hide();
     if(result.Cancelled&&result.Warnings.Count==0){summary.Text="已取消";detail.Text=result.Files.Count>0?"临时文件已清理；已提取的文件保留在结果目录。":"临时文件已清理。";}
     else if(result.Warnings.Count>0){summary.Text="解压出现异常";detail.Text=string.Join("\r\n\r\n",result.Warnings)+(result.Files.Count>0?"\r\n已提取文件保留在："+result.Output:"");}
     else if(result.Files.Count==0){summary.Text="没有匹配的文件";detail.Text="未找到符合配置规则的文件。临时文件已清理，请检查匹配规则。";}
     else {summary.Text="解压完成";detail.Text="已提取 "+result.Files.Count+" 个文件，扫描 "+result.Archives+" 个压缩包。临时文件已清理。";try{Program.OpenResult(settings,result);}catch(Exception ex){summary.Text="解压完成，但打开失败";detail.Text+="\r\n"+ex.Message;}}
     NotifyResult(result.Warnings.Count>0?ToolTipIcon.Warning:summary.Text.Contains("打开失败")?ToolTipIcon.Warning:ToolTipIcon.Info);
    } catch(Exception ex){running=false;Hide();summary.Text="解压失败";detail.Text=ex.Message;NotifyResult(ToolTipIcon.Error);}
    finally {if(!running&&!IsDisposed)Close();}
   };
  }
  void NotifyResult(ToolTipIcon kind){Notifications.Show(summary.Text+" · "+Path.GetFileName(archive),detail.Text,kind);}
  protected override void Dispose(bool disposing){if(disposing)cancellation.Dispose();base.Dispose(disposing);}
 }
}
