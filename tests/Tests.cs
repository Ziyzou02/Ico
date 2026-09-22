using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace IconController {
    public static class Tests {
        static List<string> results=new List<string>();
        static void Check(bool condition,string name){if(!condition)throw new Exception(name);results.Add("PASS "+name);}
        static void Reject(Action action,string name){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,name);}
        static int PowerShell(string script,string arguments,out string output,bool guard) {
            var info=new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+script+"\" "+arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var process=Process.Start(info)){string stdout=process.StandardOutput.ReadToEnd();string stderr=process.StandardError.ReadToEnd();if(!process.WaitForExit(30000)){process.Kill();throw new Exception("PowerShell test timed out.");}output=stdout+stderr;return process.ExitCode;}
        }
        public static int Run(string directory) {
            directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
            try {
                Check(Rules.Extension(" JL ")==".jl","extension normalization");
                foreach(string value in new[]{".exe","..txt",".txt\\shell",".typ;.jl",".txt\nwhoami",""})Reject(()=>Rules.Extension(value),"reject invalid extension "+value.Replace("\n","\\n"));
                var store=new Store(Path.Combine(directory,"library"));
                string appDir=Path.Combine(directory,"中文 O'Brien & test");Directory.CreateDirectory(appDir);
                string app=Path.Combine(appDir,"editor.exe");File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"notepad.exe"),app,true);
                Check(store.Builtins.Count==2&&store.Builtins.All(x=>File.Exists(x.IconPath)),"bundled Julia and Typst icons extracted without machine-specific paths");
                string icon=store.Builtins.Single(x=>x.Extension==".typ").IconPath;
                var p=store.Upsert(new Profile{Extension="TYP",Application=app,IconPath=icon,Note="中文备注 ' & $()\n第二行"});
                Check(p.Extension==".typ"&&p.IconPath.StartsWith(store.Root),"save normalizes and imports ICO");
                Check(store.Status(p)=="已保存","saving is not reported as applied");
                Reject(()=>store.Upsert(new Profile{Extension=".typ",Application=app,IconPath=icon}),"duplicate extension rejected");
                var loaded=new Store(store.Root);Check(loaded.Data.Profiles.Single().Note==p.Note,"repository round trip preserves Unicode and punctuation");
                var rev=store.Generate(p);Check(File.Exists(Path.Combine(rev.Folder,"apply.cmd"))&&File.Exists(Path.Combine(rev.Folder,"restore.cmd")),"apply and restore launchers generated");
                Check(File.Exists(Path.Combine(rev.Folder,"icon.ico")),"generated package contains ICO");
                Check(store.Status(p)=="已生成","generation is not reported as applied");
                string script=File.ReadAllText(Path.Combine(rev.Folder,"apply.ps1"));Check(!script.Contains("@@PAYLOAD@@")&&!script.Contains(app),"configuration embedded as data rather than PowerShell source");
                string parser=Path.Combine(directory,"parse-script.ps1");
                File.WriteAllText(parser,"$t=$null;$e=$null;[System.Management.Automation.Language.Parser]::ParseFile('"+Path.Combine(rev.Folder,"apply.ps1").Replace("'","''")+"',[ref]$t,[ref]$e)|Out-Null;if($e.Count){$e;exit 1};'PARSE_OK'",new UTF8Encoding(true));
                string output;Check(PowerShell(parser,"",out output,false)==0&&output.Contains("PARSE_OK"),"generated PowerShell parses");
                Check(PowerShell(Path.Combine(rev.Folder,"apply.ps1"),"-ValidateOnly",out output,false)==0&&output.Contains("VALIDATION_OK"),"preflight handles Unicode, apostrophe, ampersand and spaces");
                Check(!File.Exists(Path.Combine(rev.Folder,"receipt.json")),"preflight does not write an execution receipt");
                string guardScript=Path.Combine(directory,"guard-script.ps1");
                File.WriteAllText(guardScript,"$env:CODEX_THREAD_ID='self-test-isolation-guard'\r\n& '"+Path.Combine(rev.Folder,"apply.ps1").Replace("'","''")+"'",new UTF8Encoding(true));
                Check(PowerShell(guardScript,"",out output,true)!=0&&output.Contains("isolated registry"),"agent environment blocked before registry changes");
                Check(!File.Exists(Path.Combine(rev.Folder,"state.json")),"isolation guard creates no registry backup or state");
                Json.Atomic(Path.Combine(rev.Folder,"receipt.json"),new Receipt{RevisionId="wrong",Status="applied"});Check(store.Status(p)=="已生成","unrelated receipt ignored");
                Json.Atomic(Path.Combine(rev.Folder,"receipt.json"),new Receipt{RevisionId=rev.Id,Status="applied"});Check(store.Status(p)=="已执行","matching receipt marks execution");
                p.Application=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"notepad.exe");p=store.Upsert(p);Check(store.Status(p)=="已保存","edited configuration does not inherit prior execution");
                var secondRevision=store.Generate(p);
                Json.Atomic(Path.Combine(secondRevision.Folder,"receipt.json"),new Receipt{RevisionId=secondRevision.Id,Status="applied",CompletedAt=DateTime.UtcNow.AddMinutes(1).ToString("o")});
                var reverted=Json.Copy(p);reverted.Application=app;reverted=store.Upsert(reverted);
                Check(store.Status(reverted)!="已执行","reverting editor to an older profile does not revive a stale receipt");
                p=store.Upsert(p);
                string export=Path.Combine(directory,"export.json");Json.Atomic(export,new Library{Profiles=Json.Copy(store.Data.Profiles)});
                var second=new Store(Path.Combine(directory,"imported"));second.Import(export);Check(second.Data.Profiles.Count==1&&second.Data.Revisions.Count==0,"import includes profiles without execution claims");
                second.Import(export);Check(second.Data.Profiles.Count==1,"reimport merges by extension");
                var duplicate=new Library{Profiles=new List<Profile>{Json.Copy(p),Json.Copy(p)}};duplicate.Profiles[1].Id=Guid.NewGuid().ToString("N");Json.Atomic(export,duplicate);
                Reject(()=>second.Import(export),"duplicate import rejected before changing library");Check(second.Data.Profiles.Count==1,"failed import leaves repository unchanged");
                second.Delete(second.Data.Profiles[0].Id);Check(File.Exists(icon)&&File.Exists(Path.Combine(rev.Folder,"apply.ps1")),"removing repository entry keeps scripts and source assets");
                File.WriteAllText(Path.Combine(second.Root,"library.json"),"broken JSON");bool error=false;try{new Store(second.Root);}catch(IOException){error=true;}
                Check(error&&File.ReadAllText(Path.Combine(second.Root,"library.json"))=="broken JSON","corrupt repository is reported without overwriting it");
                results.Add("ALL TESTS PASSED");File.WriteAllLines(Path.Combine(directory,"results.txt"),results,new UTF8Encoding(true));return 0;
            }catch(Exception e){results.Add("FAIL "+e);File.WriteAllLines(Path.Combine(directory,"results.txt"),results,new UTF8Encoding(true));return 1;}
        }
    }
}
