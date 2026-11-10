using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace PrecisionExtract {
 public class Settings {
  public string Output = "";
  public bool Regex = true;
  public bool OpenEditor = false;
  public bool OpenDirectory = true;
  public string Editor = "";
  public List<string> Rules = new List<string>();
  public static Settings Defaults() {return new Settings {Rules=new List<string> { @"^application.*\.log$", @"^error.*\.log$", @"config\.xml$" }};}
  public static string DirectoryPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrecisionExtract"); } }
  static string FilePath { get { return Path.Combine(DirectoryPath, "settings.xml"); } }
  public string OutputPath { get { return string.IsNullOrWhiteSpace(Output) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "精准解压") : Path.GetFullPath(Environment.ExpandEnvironmentVariables(Output)); } }
  public static Settings Load() {
   using(var mutex = new Mutex(false, @"Local\PrecisionExtract.Settings")) {
    Lock(mutex); try { if (!File.Exists(FilePath)) return Defaults(); using(var s = File.OpenRead(FilePath)) return (Settings)new XmlSerializer(typeof(Settings)).Deserialize(s); } finally { mutex.ReleaseMutex(); }
   }
  }
  internal static void Lock(Mutex m) { try { if(!m.WaitOne(15000)) throw new IOException("等待配置文件超时。"); } catch(AbandonedMutexException) {} }
  public void Save() {
   using(var mutex = new Mutex(false, @"Local\PrecisionExtract.Settings")) {
    Lock(mutex); string tmp = null;
    try { Directory.CreateDirectory(DirectoryPath); tmp = Path.Combine(DirectoryPath, Guid.NewGuid()+".xml"); using(var s = File.Create(tmp)) new XmlSerializer(typeof(Settings)).Serialize(s,this); if(File.Exists(FilePath)) File.Replace(tmp,FilePath,null); else File.Move(tmp,FilePath); }
    finally { if(tmp!=null && File.Exists(tmp)) File.Delete(tmp); mutex.ReleaseMutex(); }
   }
  }
 }
 public class ExtractResult {
  public string Output = "";
  public List<string> Files = new List<string>();
  public List<string> Warnings = new List<string>();
  public bool Cancelled;
  public int Archives;
 }
 public class Entry { public string Name; public long Size; public bool Folder; }
 public sealed class Extractor {
  public static readonly string Engine = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"engine","7z.exe");
  readonly Settings settings;
  readonly Action<string> report;
  readonly CancellationToken token;
  readonly List<Regex> patterns;
  readonly ExtractResult result = new ExtractResult();
  string temp;
  long expanded;
  int entries;
  // Explicit limits fail visibly instead of silently truncating nested searches.
  const long MaxBytes = 20L*1024*1024*1024;
  const int MaxEntries = 100000;
  public Extractor(Settings s, Action<string> progress, CancellationToken cancellation) {
   settings=s; report=progress; token=cancellation;
   if(s.Rules==null || !s.Rules.Any(x=>!string.IsNullOrWhiteSpace(x))) throw new ArgumentException("请至少添加一条文件规则。");
   patterns=s.Regex?s.Rules.Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>new Regex(x,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1))).ToList():new List<Regex>();
  }
  public static bool IsArchive(string name) {
   string[] suffix={".zip",".tar",".rar",".7z",".gz",".gzip",".tgz",".bz2",".tbz2",".xz",".txz",".zst",".zstd"};
   return suffix.Any(x=>name.EndsWith(x,StringComparison.OrdinalIgnoreCase));
  }
  bool Match(string name) { return settings.Regex?patterns.Any(r=>r.IsMatch(name)):settings.Rules.Any(r=>string.Equals(r.Trim(),name,StringComparison.OrdinalIgnoreCase)); }
  static string BaseName(string path) { return path.Replace('\\','/').Split('/').Last(); }
  static string Quote(string value) { // Windows CommandLineToArgvW escaping
   var b=new StringBuilder("\""); int slashes=0;
   foreach(char c in value) { if(c=='\\') {slashes++;continue;} if(c=='"') {b.Append('\\',slashes*2+1); b.Append(c);} else {b.Append('\\',slashes);b.Append(c);} slashes=0; }
   b.Append('\\',slashes*2);b.Append('"');return b.ToString();
  }
  Process Start(params string[] args) {
   token.ThrowIfCancellationRequested();
   var p=new Process { StartInfo=new ProcessStartInfo(Engine,string.Join(" ",args.Select(Quote))) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8} };
   p.Start();p.StandardInput.Close();return p;
  }
  static void Kill(Process p) { try { if(!p.HasExited)p.Kill(); } catch(InvalidOperationException) {} }
  List<Entry> List(string archive) {
   using(var p=Start("l","-slt","-ba","-sccUTF-8","-p__NO_PASSWORD__","--",archive))
   using(token.Register(()=>Kill(p))) {
    var stderr=p.StandardError.ReadToEndAsync(); var list=new List<Entry>(); Entry entry=null; string line;
    try {
    while((line=p.StandardOutput.ReadLine())!=null) {
     token.ThrowIfCancellationRequested();
     if(line.StartsWith("Path = ")) { if(++entries>MaxEntries)throw new IOException("压缩包条目超过 100000，已停止。");entry=new Entry{Name=line.Substring(7)};list.Add(entry); }
     else if(entry!=null && line.StartsWith("Size = ")) { long size;if(long.TryParse(line.Substring(7),out size))entry.Size=size; }
     else if(entry!=null && line.StartsWith("Folder = "))entry.Folder=line.EndsWith("+");
     else if(entry!=null && line.StartsWith("Attributes = ") && line.Substring(13).TrimStart().StartsWith("D")) entry.Folder=true;
     else if(entry!=null && ((line.StartsWith("Symbolic Link = ")&&!string.IsNullOrWhiteSpace(line.Substring(16)))||(line.StartsWith("Hard Link = ")&&!string.IsNullOrWhiteSpace(line.Substring(12))))) throw new IOException("不支持压缩包中的链接条目。");
    }
    p.WaitForExit();token.ThrowIfCancellationRequested();if(p.ExitCode!=0)throw new IOException("无法读取压缩包（损坏、加密或格式不受支持）。"+stderr.GetAwaiter().GetResult());
    return list;
    } finally { Kill(p); }
   }
  }
  void ExtractEntry(string archive,Entry entry,string path) {
   if(entry.Size<0 || entry.Size>MaxBytes-expanded)throw new IOException("本次展开数据超过 20 GB 限额，已停止。");
   using(var p=Start("x","-so","-spd","-y","-p__NO_PASSWORD__","--",archive,entry.Name))
   using(token.Register(()=>Kill(p))) {
    var stderr=p.StandardError.ReadToEndAsync();
    try {
     using(var target=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
      var buffer=new byte[65536];int count;
      while((count=p.StandardOutput.BaseStream.Read(buffer,0,buffer.Length))>0) {
       token.ThrowIfCancellationRequested();expanded+=count;if(expanded>MaxBytes)throw new IOException("本次展开数据超过 20 GB 限额，已停止。");target.Write(buffer,0,count);
      }
     }
     p.WaitForExit();token.ThrowIfCancellationRequested();if(p.ExitCode!=0)throw new IOException("提取失败："+entry.Name+"\n"+stderr.GetAwaiter().GetResult());
     if(new FileInfo(path).Length!=entry.Size)throw new IOException("文件大小校验失败："+entry.Name);
    } finally { Kill(p); }
   }
  }
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
  static extern bool CreateDirectory(string path,IntPtr attributes);
  public static string ReserveOutput(string root) {
   string day=Path.Combine(root,DateTime.Now.ToString("yyyy-MM-dd"));Directory.CreateDirectory(day);
   // CreateDirectory is atomic across all processes. Never use Exists + CreateDirectory.
   int start=1;
   foreach(string existing in Directory.EnumerateFileSystemEntries(day,"问题*")) {int n;if(int.TryParse(Path.GetFileName(existing).Substring(2),out n)&&n>=start&&n<int.MaxValue)start=n+1;}
   for(int i=start;i<int.MaxValue;i++) {
    string candidate=Path.Combine(day,"问题"+i);
    if(CreateDirectory(candidate,IntPtr.Zero))return candidate;
    int error=Marshal.GetLastWin32Error();if(error!=183 && error!=80)throw new System.ComponentModel.Win32Exception(error,"无法创建输出目录："+candidate);
   }
   throw new IOException("目录编号已用尽。");
  }
  string OutputFile(string name) {
   var invalid=Path.GetInvalidFileNameChars();string safe=new string(name.Select(c=>invalid.Contains(c)?'_':c).ToArray()).TrimEnd('.',' ');
   if(string.IsNullOrEmpty(safe))safe="file";
   if(safe.Length>150)safe=safe.Substring(0,110)+Path.GetExtension(safe).Substring(0,Math.Min(20,Path.GetExtension(safe).Length));
   if(Regex.IsMatch(safe,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase))safe="_"+safe;
   if(string.IsNullOrEmpty(result.Output))result.Output=ReserveOutput(settings.OutputPath);
   string path=Path.Combine(result.Output,safe);int i=2;
   while(File.Exists(path))path=Path.Combine(result.Output,Path.GetFileNameWithoutExtension(safe)+" ("+(i++)+")"+Path.GetExtension(safe));
   return path;
  }
  void Walk(string archive,int depth,string source) {
   token.ThrowIfCancellationRequested();if(depth>128)throw new IOException("嵌套超过 128 层，已停止。");
   report("正在扫描第 "+(depth+1)+" 层："+source);result.Archives++;
   foreach(var entry in List(archive)) {
    token.ThrowIfCancellationRequested();if(entry.Folder)continue;
    string name=BaseName(entry.Name);bool nested=IsArchive(name),match=Match(name);
    if(!nested && !match)continue;
    string staged=Path.Combine(temp,Guid.NewGuid().ToString("N")+Path.GetExtension(name));
    try {
     report("正在提取："+name);ExtractEntry(archive,entry,staged);
     if(match) {string output=OutputFile(name);try{File.Copy(staged,output,false);result.Files.Add(output);}catch{if(File.Exists(output))File.Delete(output);throw;}}
     if(nested) {try {Walk(staged,depth+1,source+" → "+name);} catch(OperationCanceledException) {throw;} catch(Exception e) {result.Warnings.Add(source+" → "+name+"："+e.Message);} }
    } finally { if(File.Exists(staged))File.Delete(staged); }
   }
  }
  public ExtractResult Run(string archive) {
   if(!File.Exists(Engine))throw new FileNotFoundException("缺少解压引擎，请保留 engine 文件夹。",Engine);
   if(!File.Exists(archive))throw new FileNotFoundException("压缩文件不存在。",archive);
   temp=Path.Combine(Path.GetTempPath(),"PrecisionExtract",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
   try { Walk(Path.GetFullPath(archive),0,Path.GetFileName(archive)); }
   catch(OperationCanceledException) {result.Cancelled=true;}
   catch(Exception e) {result.Warnings.Add(e.Message);}
   finally {report("正在清理临时文件…");try {Directory.Delete(temp,true);}catch(Exception e){result.Warnings.Add("临时文件清理失败："+temp+"\n"+e.Message);} }
   return result;
  }
 }
}
