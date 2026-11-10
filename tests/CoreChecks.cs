using System;
using System.IO;
using System.Linq;
using System.Threading;
using PrecisionExtract;
class CoreChecks {
 static int Main(string[] args) {
  var root=Path.Combine(Path.GetTempPath(),"PrecisionExtract");
  var before=Directory.Exists(root)?Directory.GetDirectories(root):new string[0];
  using(var cts=new CancellationTokenSource()) {
   var s=Settings.Defaults();s.Output=args[1];
   var r=new Extractor(s,text=>{if(text.StartsWith("正在扫描第 2 层"))cts.Cancel();},cts.Token).Run(args[0]);
   if(!r.Cancelled)throw new Exception("Nested task did not cancel");
   if(r.Warnings.Count>0)throw new Exception(string.Join(";",r.Warnings));
   if(Directory.GetDirectories(root).Except(before).Any())throw new Exception("Cancellation leaked temporary files");
  }
  var saved=new Settings();saved.Rules.Add("only-this.log");saved.OpenDirectory=false;
  var serializer=new System.Xml.Serialization.XmlSerializer(typeof(Settings));
  using(var stream=new MemoryStream()) {serializer.Serialize(stream,saved);stream.Position=0;var loaded=(Settings)serializer.Deserialize(stream);if(loaded.Rules.Count!=1||loaded.Rules[0]!="only-this.log")throw new Exception("Rules changed after configuration reload");if(loaded.OpenDirectory)throw new Exception("OpenDirectory=false did not persist");}
  using(var reader=new StringReader("<Settings><Output /></Settings>")){var old=(Settings)serializer.Deserialize(reader);if(!old.OpenDirectory)throw new Exception("Legacy settings changed their default directory-opening behavior");}
  Console.WriteLine("PASS cancellation while nested archive is staged, cleanup and settings round-trip");return 0;
 }
}
