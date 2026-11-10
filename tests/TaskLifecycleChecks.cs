using System;
using System.Reflection;
using System.Windows.Forms;
using PrecisionExtract;
class TaskLifecycleChecks {
 [STAThread] static int Main(string[] args) {
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  string[] archives={args[0],args[0],args[0]+".missing"};
  string[] rules={"\\.log$","^not-a-match$","\\.log$"};
  var assembly=typeof(Settings).Assembly;
  var contextType=assembly.GetType("PrecisionExtract.TaskContext");
  for(int i=0;i<archives.Length;i++) {
   var s=new Settings {Output=args[1],OpenDirectory=false,OpenEditor=false};s.Rules.Add(rules[i]);
   using(var context=(ApplicationContext)Activator.CreateInstance(contextType,new object[]{s,archives[i]}))
   using(var timer=new System.Windows.Forms.Timer{Interval=50}) {
    DateTime started=DateTime.UtcNow;bool verified=false;Exception failure=null;
    timer.Tick+=(a,b)=>{
     if(Application.OpenForms.Count==0){
      var icon=(NotifyIcon)assembly.GetType("PrecisionExtract.Notifications").GetField("icon",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
      if(icon==null||!icon.Visible)failure=new Exception("Task window closed without live Windows notification icon");
      verified=true;timer.Stop();context.ExitThread();
     }else if((DateTime.UtcNow-started).TotalSeconds>10){failure=new Exception("Task result window remained visible after completion");timer.Stop();context.ExitThread();}
    };
    timer.Start();Application.Run(context);
    if(failure!=null)throw failure;
    if(!verified)throw new Exception("Notification lifetime ended before lifecycle check");
   }
  }
  Console.WriteLine("PASS success, no-match and failure: task windows close, notification icons remain alive");return 0;
 }
}
