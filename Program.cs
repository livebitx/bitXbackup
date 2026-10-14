using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

[DataContract] class Settings {
 [DataMember] public int Version=1;
 [DataMember] public string[] QuickFolders=new string[0];
 [DataMember] public string[] CustomFolders=new string[0];
 [DataMember] public bool Encrypt;
}
static class Core {
 public static string Quote(string value){var output=new StringBuilder("\"");int slashes=0;foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"'){output.Append('\\',slashes*2+1);output.Append(c);}else{output.Append('\\',slashes);output.Append(c);}slashes=0;}output.Append('\\',slashes*2);output.Append('"');return output.ToString();}
 public static bool Within(string path,string parent){path=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);parent=Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);return path.Equals(parent,StringComparison.OrdinalIgnoreCase)||path.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
 public static List<string> Normalize(IEnumerable<string> paths){var items=paths.Select(Path.GetFullPath).Select(p=>p.TrimEnd('\\')+"\\").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p=>p.Length).ToList();var output=new List<string>();foreach(var path in items){if(!output.Any(p=>Within(path,p)))output.Add(path);}return output;}
 public static void SaveSettings(string file,Settings data){using(var stream=File.Create(file)){new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream,data);}}
 public static Settings LoadSettings(string file){using(var stream=File.OpenRead(file)){var data=(Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);if(data==null||data.Version!=1)throw new Exception("Unsupported settings version.");data.QuickFolders=data.QuickFolders??new string[0];data.CustomFolders=data.CustomFolders??new string[0];return data;}}
 public static void CopyFolder(string source,string target,CancellationToken cancel,Action<string> progress){
  cancel.ThrowIfCancellationRequested();if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new Exception("Directory link is not supported: "+source);
  Directory.CreateDirectory(target);
  foreach(var file in Directory.GetFiles(source)){cancel.ThrowIfCancellationRequested();if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new Exception("File link is not supported: "+file);File.Copy(file,Path.Combine(target,Path.GetFileName(file)),false);File.SetAttributes(Path.Combine(target,Path.GetFileName(file)),FileAttributes.Normal);File.SetLastWriteTimeUtc(Path.Combine(target,Path.GetFileName(file)),File.GetLastWriteTimeUtc(file));}
  foreach(var dir in Directory.GetDirectories(source)){CopyFolder(dir,Path.Combine(target,Path.GetFileName(dir)),cancel,progress);}
 }
 public static int Run(string binary,string directory,string args,CancellationToken cancel){
  var start=new ProcessStartInfo(binary,args){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
  using(var child=Process.Start(start)){
   var output=child.StandardOutput.ReadToEndAsync();var errors=child.StandardError.ReadToEndAsync();
   while(!child.WaitForExit(150)){if(cancel.IsCancellationRequested){try{child.Kill();}catch{}child.WaitForExit();cancel.ThrowIfCancellationRequested();}}
   Task.WaitAll(output,errors);return child.ExitCode;
  }
 }
 public static void Test(string binary){
  var root=Path.Combine(Path.GetTempPath(),"bitXbackup_test_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   var source=Path.Combine(root,"source");Directory.CreateDirectory(source);Directory.CreateDirectory(Path.Combine(source,"child"));File.WriteAllText(Path.Combine(source,"child","sample.txt"),"backup test");
   if(Normalize(new[]{source,source,Path.Combine(source,"child")}).Count!=1)throw new Exception("Folder normalization failed.");
   var copy=Path.Combine(root,"copy");CopyFolder(source,copy,CancellationToken.None,s=>{});if(File.ReadAllText(Path.Combine(copy,"child","sample.txt"))!="backup test")throw new Exception("Copy test failed.");
   string slashPath="C:"+new string('\\',1);string quotedPath="\"C:"+new string('\\',2)+"\"";
   if(Quote("a b")!="\"a b\""||Quote(slashPath)!=quotedPath)throw new Exception("Argument quoting failed.");
   var cfg=Path.Combine(root,"settings.json");SaveSettings(cfg,new Settings{QuickFolders=new[]{"WireGuard"},CustomFolders=new[]{source},Encrypt=true});var restored=LoadSettings(cfg);if(!restored.Encrypt||restored.QuickFolders[0]!="WireGuard"||restored.CustomFolders[0]!=source)throw new Exception("Settings roundtrip failed.");
   if(!Within(source,root)||Within(root,source))throw new Exception("Path validation failed.");
   foreach(bool encrypted in new[]{false,true}){
    var archive=Path.Combine(root,encrypted?"encrypted.7z":"plain.7z");var flags=encrypted?" -mhe=on "+Quote("-ptest password"):"";
    if(Run(binary,copy,"a -t7z -mx=1 -y -bd "+flags+" -- "+Quote(archive)+" .\\*",CancellationToken.None)!=0)throw new Exception("7-Zip creation test failed.");
    if(Run(binary,copy,"t -y -bd "+(encrypted?Quote("-ptest password"):"")+" -- "+Quote(archive),CancellationToken.None)!=0)throw new Exception("7-Zip integrity test failed.");
    if(encrypted&&Run(binary,copy,"t -y -bd -pwrong -- "+Quote(archive),CancellationToken.None)==0)throw new Exception("Wrong archive password was accepted.");
   }
  }finally{Directory.Delete(root,true);}
 }
}
class BackupForm:Form {
 const string Version="0.1.0";
 TextBox destination=new TextBox(),password=new TextBox(),confirm=new TextBox();
 CheckBox encryption=new CheckBox();CheckedListBox quick=new CheckedListBox();ListBox folders=new ListBox();RichTextBox log=new RichTextBox();
 Dictionary<string,List<string>> presets=new Dictionary<string,List<string>>();
 List<string> presetNames=new List<string>();List<Control> controls=new List<Control>();Button stop;CancellationTokenSource cancellation;bool running;string binary,work;
 public BackupForm(string archiveBinary,string temporary){
  binary=archiveBinary;work=temporary;Text="bitXbackup "+Version;ClientSize=new Size(920,690);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
  Icon=new Icon(Path.Combine(work,"bitXbackup.ico"));
  LabelAt("Quick folders",15,15);LabelAt("Additional folders",360,15);
  quick.SetBounds(15,42,330,170);quick.CheckOnClick=true;Controls.Add(quick);controls.Add(quick);
  folders.SetBounds(360,42,545,170);folders.HorizontalScrollbar=true;Controls.Add(folders);controls.Add(folders);
  AddPreset("Desktop",new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)},true);
  AddPreset("Documents",new[]{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)},true);
  var downloads=Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders","{374DE290-123F-4565-9164-39C4925E467B}",null) as string;
  AddPreset("Downloads",new[]{downloads==null?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads"):Environment.ExpandEnvironmentVariables(downloads)},true);
  AddPreset("OpenVPN",new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"OpenVPN","config"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"OpenVPN","config"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"OpenVPN","config-auto"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"OpenVPN","config")},false);
  AddPreset("WireGuard",new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WireGuard","Data","Configurations")},false);
  ButtonAt("Add folder",360,224,()=>{using(var dialog=new FolderBrowserDialog()){if(dialog.ShowDialog(this)==DialogResult.OK&&!folders.Items.Contains(dialog.SelectedPath))folders.Items.Add(dialog.SelectedPath);}});
  ButtonAt("Remove",500,224,()=>{if(folders.SelectedIndex>=0)folders.Items.RemoveAt(folders.SelectedIndex);});
  ButtonAt("Import list",640,224,Import);ButtonAt("Export list",780,224,Export);
  LabelAt("Destination",15,277);destination.SetBounds(135,270,630,26);Controls.Add(destination);controls.Add(destination);
  ButtonAt("Browse...",780,267,()=>{using(var dialog=new FolderBrowserDialog()){if(dialog.ShowDialog(this)==DialogResult.OK)destination.Text=dialog.SelectedPath;}});
  encryption.Text="Encrypt archive and file names";encryption.SetBounds(15,315,280,25);Controls.Add(encryption);controls.Add(encryption);
  LabelAt("Password",300,320);password.SetBounds(385,314,180,26);password.UseSystemPasswordChar=true;Controls.Add(password);controls.Add(password);
  LabelAt("Confirm",580,320);confirm.SetBounds(665,314,240,26);confirm.UseSystemPasswordChar=true;Controls.Add(confirm);controls.Add(confirm);
  encryption.CheckedChanged+=(s,e)=>{password.Enabled=confirm.Enabled=encryption.Checked;};password.Enabled=confirm.Enabled=false;
  ButtonAt("Create backup",15,360,Start);stop=ButtonAt("Stop",155,360,()=>{if(cancellation!=null)cancellation.Cancel();});stop.Enabled=false;
  ButtonAt("Open destination",295,360,OpenDestination);ButtonAt("Run as administrator",435,360,Admin);ButtonAt("About",640,360,About);
  log.SetBounds(15,412,890,260);log.ReadOnly=true;log.WordWrap=true;log.ScrollBars=RichTextBoxScrollBars.Vertical;log.Font=new Font("Consolas",9);Controls.Add(log);
  FormClosing+=(s,e)=>{if(running){cancellation.Cancel();e.Cancel=true;}else{password.Clear();confirm.Clear();}};
 }
 void LabelAt(string text,int x,int y){var label=new Label{Text=text,AutoSize=true,Location=new Point(x,y)};Controls.Add(label);}
 Button ButtonAt(string text,int x,int y,Action action){var button=new Button{Text=text,Location=new Point(x,y),Size=new Size(text=="Run as administrator"?190:125,30)};button.Click+=(s,e)=>action();Controls.Add(button);controls.Add(button);return button;}
 void AddPreset(string name,IEnumerable<string> paths,bool check){var existing=paths.Where(p=>!String.IsNullOrEmpty(p)&&Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();presets[name]=existing;presetNames.Add(name);quick.Items.Add(name+(existing.Count==0?" (not found)":""),check&&existing.Count>0);}
 Settings Current(){return new Settings{QuickFolders=Enumerable.Range(0,quick.Items.Count).Where(i=>quick.GetItemChecked(i)).Select(i=>presetNames[i]).ToArray(),CustomFolders=folders.Items.Cast<string>().ToArray(),Encrypt=encryption.Checked};}
 void Import(){try{using(var dialog=new OpenFileDialog{Filter="Backup lists (*.json)|*.json"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;var data=Core.LoadSettings(dialog.FileName);for(int i=0;i<quick.Items.Count;i++)quick.SetItemChecked(i,data.QuickFolders.Contains(presetNames[i]));folders.Items.Clear();folders.Items.AddRange(data.CustomFolders);encryption.Checked=data.Encrypt;password.Clear();confirm.Clear();Log("Imported folder list. VPN selections are resolved on this PC.");}}catch(Exception e){Log("ERROR: "+e.Message);}}
 void Export(){try{using(var dialog=new SaveFileDialog{Filter="Backup lists (*.json)|*.json",FileName="bitXbackup-list.json"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;Core.SaveSettings(dialog.FileName,Current());Log("Exported folder list. No passwords or VPN file contents are included.");}}catch(Exception e){Log("ERROR: "+e.Message);}}
 void OpenDestination(){try{if(!Directory.Exists(destination.Text))throw new Exception("Select an existing destination folder.");Process.Start("explorer.exe",Core.Quote(destination.Text));}catch(Exception e){Log("ERROR: "+e.Message);}}
 void Admin(){try{var start=new ProcessStartInfo(Application.ExecutablePath){UseShellExecute=true,Verb="runas"};Process.Start(start);Close();}catch(Exception e){Log("ERROR: "+e.Message);}}
 void Log(string text){if(InvokeRequired){BeginInvoke(new Action<string>(Log),text);return;}log.SelectionStart=log.TextLength;log.SelectionColor=text.StartsWith("ERROR:")?Color.Firebrick:text.StartsWith("DONE:")?Color.ForestGreen:Color.Black;log.AppendText(text+"\r\n\r\n");log.ScrollToCaret();}
 void Busy(bool value){running=value;foreach(var c in controls)c.Enabled=!value;stop.Enabled=value;if(!value)password.Enabled=confirm.Enabled=encryption.Checked;}
 async void Start(){
  string stage=null,partial=null;string secret=null;
  try{
   var config=Current();var inputs=new List<string>(config.CustomFolders);foreach(var name in config.QuickFolders){if(presets[name].Count==0)throw new Exception(name+" was selected but no folder was found.");inputs.AddRange(presets[name]);}
   var sources=Core.Normalize(inputs);if(sources.Count==0)throw new Exception("Select at least one folder.");
   foreach(var path in sources)if(!Directory.Exists(path))throw new Exception("Folder not found: "+path);
   if(String.IsNullOrWhiteSpace(destination.Text)||!Path.IsPathRooted(destination.Text))throw new Exception("Choose an absolute destination folder.");
   string output=Path.GetFullPath(destination.Text);if(sources.Any(p=>Core.Within(output,p)))throw new Exception("The destination must be outside every selected source folder.");
   if(config.Encrypt){if(password.Text.Length==0||password.Text!=confirm.Text)throw new Exception("Enter matching, non-empty passwords.");secret=password.Text;}
   Directory.CreateDirectory(output);string id=DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")+"_"+Guid.NewGuid().ToString("N").Substring(0,6);string final=Path.Combine(output,"backup_"+id+".7z");partial=final+".partial";stage=Path.Combine(output,".bitXbackup_"+id);Directory.CreateDirectory(stage);
   cancellation=new CancellationTokenSource();Busy(true);Log("Preparing "+sources.Count+" source folder(s).");
   string staging=stage,archive=partial,pw=secret;bool encrypt=config.Encrypt;var token=cancellation.Token;
   await Task.Run(()=>{
    for(int i=0;i<sources.Count;i++){token.ThrowIfCancellationRequested();string path=sources[i];string name=Path.GetFileName(path.TrimEnd('\\'));if(String.IsNullOrEmpty(name))name="root";Log("Copying: "+path);Core.CopyFolder(path,Path.Combine(staging,(i+1).ToString("00")+"_"+name),token,Log);}
    File.WriteAllLines(Path.Combine(staging,"original-paths.txt"),sources.Select((p,i)=>(i+1).ToString("00")+": "+p),new UTF8Encoding(false));
    string flags=encrypt?" -mhe=on "+Core.Quote("-p"+pw):"";
    Log("Creating 7z archive...");int code=Core.Run(binary,staging,"a -t7z -mx=3 -y -bd -bb0 "+flags+" -- "+Core.Quote(archive)+" .\\*",token);if(code!=0)throw new Exception("7-Zip backup failed (exit code "+code+"). Open files or permissions may have prevented a complete backup.");
    Log("Testing archive integrity...");code=Core.Run(binary,staging,"t -y -bd -bb0 "+(encrypt?Core.Quote("-p"+pw):"")+" -- "+Core.Quote(archive),token);if(code!=0)throw new Exception("Archive integrity test failed (exit code "+code+").");token.ThrowIfCancellationRequested();
   });
   File.Move(partial,final);partial=null;Log("DONE: "+final);
  }catch(OperationCanceledException){Log("ERROR: Backup stopped. No final archive was published.");}catch(Exception e){Log("ERROR: "+e.Message);}finally{
   secret=null;password.Clear();confirm.Clear();
   try{if(partial!=null&&File.Exists(partial))File.Delete(partial);if(stage!=null&&Directory.Exists(stage))Directory.Delete(stage,true);}catch(Exception e){Log("ERROR: Temporary file cleanup failed: "+e.Message);}
   Busy(false);if(cancellation!=null){cancellation.Dispose();cancellation=null;}
  }
 }
 void About(){using(var dialog=new Form{Text="About bitXbackup",ClientSize=new Size(440,230),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false,StartPosition=FormStartPosition.CenterParent,Icon=Icon}){
  var label=new Label{Text="bitXbackup "+Version+"\r\nFolder backups powered by 7-Zip.\r\nOptional archive and file-name encryption.",Location=new Point(20,20),Size=new Size(400,80)};dialog.Controls.Add(label);
  string[] titles={"GitHub","bitx.ro","Support via Revolut"};string[] links={"https://github.com/livebitx/bitXbackup","https://bitx.ro","https://revolut.me/bin"};for(int i=0;i<links.Length;i++){string url=links[i];var link=new LinkLabel{Text=titles[i],AutoSize=true,Location=new Point(20,105+i*28)};link.LinkClicked+=(s,e)=>Process.Start(url);dialog.Controls.Add(link);}dialog.ShowDialog(this);
 }}
}
static class Program {
 [System.Runtime.InteropServices.DllImport("shell32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern int SetCurrentProcessExplicitAppUserModelID(string id);
 static void Extract(string resource,string path){using(var input=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))using(var output=File.Create(path)){input.CopyTo(output);}}
 [STAThread] static int Main(string[] args){string work=null;try{
  if(args.Contains("--test")){Core.Test(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"7z.exe"));return 0;}
  SetCurrentProcessExplicitAppUserModelID("bitX.bitXbackup");Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  work=Path.Combine(Path.GetTempPath(),"bitXbackup_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);Extract("bitXbackup.ico",Path.Combine(work,"bitXbackup.ico"));
  string root=Path.GetDirectoryName(Application.ExecutablePath),binary=Path.Combine(root,"7z.exe");if(!File.Exists(binary)||!File.Exists(Path.Combine(root,"7z.dll"))){binary=Path.Combine(work,"7z.exe");Extract("7z.exe",binary);Extract("7z.dll",Path.Combine(work,"7z.dll"));}
  Application.Run(new BackupForm(binary,work));return 0;
 }catch(Exception e){if(args.Contains("--test")){Console.Error.WriteLine(e.ToString());}else{MessageBox.Show(e.Message,"bitXbackup error");}return 1;}finally{if(work!=null){try{Directory.Delete(work,true);}catch{}}}}
}
