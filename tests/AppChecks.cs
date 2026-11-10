using System;
using System.Collections.Generic;
using System.Reflection;
using PrecisionExtract;
class AppChecks {
 static int Main() {
  var method=typeof(Settings).Assembly.GetType("PrecisionExtract.Program").GetMethod("OpenResult",BindingFlags.Static|BindingFlags.Public);
  for(int bits=0;bits<4;bits++) {
   var s=new Settings{OpenDirectory=(bits&1)!=0,OpenEditor=(bits&2)!=0};
   var r=new ExtractResult{Output="test-output",Files=new List<string>{"test.log"}};
   int dirs=0,editors=0;
   Action<string> openDir=p=>{if(p!="test-output")throw new Exception("Wrong directory");dirs++;};
   Action<Settings,List<string>> openEditor=(config,files)=>{if(files.Count!=1)throw new Exception("Wrong editor files");editors++;};
   method.Invoke(null,new object[]{s,r,openDir,openEditor});
   if(dirs!=(s.OpenDirectory?1:0)||editors!=(s.OpenEditor?1:0))throw new Exception("Completion switches are not independent");
   r.Warnings.Add("failed");dirs=editors=0;method.Invoke(null,new object[]{s,r,openDir,openEditor});
   if(dirs!=0||editors!=0)throw new Exception("Failed task auto-opened output");
  }
  Console.WriteLine("PASS all four directory/editor switch combinations and no auto-open on failure");
  var launchMethod=typeof(Settings).Assembly.GetType("PrecisionExtract.Program").GetMethod("LaunchEditor",BindingFlags.Static|BindingFlags.Public);
  var launches=new List<string>();
  Action<string,string> launch=(exe,arguments)=>{if(exe!="notepad++.exe")throw new Exception("Wrong editor");launches.Add(arguments);};
  launchMethod.Invoke(null,new object[]{"notepad++.exe",new[]{@"D:\输出 目录\application.log",@"D:\输出 目录\error.log"},launch});
  if(launches.Count!=1||launches[0]!="-multiInst -nosession \"D:\\输出 目录\\application.log\" \"D:\\输出 目录\\error.log\"")throw new Exception("Expected one new, clean instance with both quoted files");
  launches.Clear();
  launchMethod.Invoke(null,new object[]{"notepad++.exe",new[]{new string('a',12000),new string('b',12000)},launch});
  if(launches.Count!=2||launches.Exists(args=>!args.StartsWith("-multiInst -nosession ")))throw new Exception("Overflow batch reused existing instance");
  Console.WriteLine("PASS Notepad++ fresh instance, no restored session, quoted filenames and oversized batches");return 0;
 }
}
