using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IconController {
    [DataContract] public class BuiltinIcon {
        [DataMember] public string Name;
        [DataMember] public string Extension;
        [DataMember] public string Resource;
        public string IconPath;
        public override string ToString() { return Name + " (" + Extension + ")"; }
    }
    [DataContract] public class Profile {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Extension = "";
        [DataMember] public string Application = "";
        [DataMember] public string IconPath = "";
        [DataMember] public int IconIndex;
        [DataMember] public string Note = "";
        [DataMember] public string UpdatedAt = "";
        [DataMember] public bool ConfirmedExisting;
    }
    [DataContract] public class Revision {
        [DataMember] public string Id;
        [DataMember] public string ProfileId;
        [DataMember] public string Fingerprint;
        [DataMember] public string CreatedAt;
        [DataMember] public string Folder;
        [DataMember(EmitDefaultValue=false)] public string ScriptFolder;
    }
    [DataContract] public class Library {
        [DataMember] public int Version = 1;
        [DataMember] public List<Profile> Profiles = new List<Profile>();
        [DataMember] public List<Revision> Revisions = new List<Revision>();
    }
    [DataContract] public class Receipt {
        [DataMember(Name="revisionId")] public string RevisionId;
        [DataMember(Name="status")] public string Status;
        [DataMember(Name="completedAt")] public string CompletedAt;
        [DataMember(Name="message")] public string Message;
        [DataMember(Name="backupFolder")] public string BackupFolder;
    }
    [DataContract] public class Payload {
        [DataMember(Name="extension")] public string Extension;
        [DataMember(Name="application")] public string Application;
        [DataMember(Name="iconPath")] public string IconPath;
        [DataMember(Name="iconIndex")] public int IconIndex;
        [DataMember(Name="bundledIcon")] public bool BundledIcon;
        [DataMember(Name="iconData")] public string IconData;
        [DataMember(Name="revisionId")] public string RevisionId;
    }
    public static class Json {
        public static byte[] Bytes<T>(T value) { using(var s=new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(s,value); return s.ToArray(); } }
        public static T Read<T>(byte[] value) { using(var s=new MemoryStream(value)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(s); }
        public static T Copy<T>(T value) { return Read<T>(Bytes(value)); }
        public static void Atomic<T>(string path,T value) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try { File.WriteAllBytes(temp,Bytes(value)); if(File.Exists(path)) File.Replace(temp,path,path+".bak"); else File.Move(temp,path); }
            finally { if(File.Exists(temp)) File.Delete(temp); }
        }
    }
    public static class Rules {
        public static string Extension(string value) {
            string ext=(value??"").Trim().ToLowerInvariant();
            if(!ext.StartsWith(".")) ext="."+ext;
            if(!Regex.IsMatch(ext,@"^\.[a-z0-9][a-z0-9_+-]{0,31}$")) throw new ArgumentException("请输入单个后缀，例如 .typ 或 .jl（不支持路径或多个后缀）。");
            if(new[]{".exe",".com",".lnk",".dll",".sys",".cpl",".scr"}.Contains(ext)) throw new ArgumentException("此后缀用于 Windows 程序或系统组件，本应用不修改其打开关联。");
            return ext;
        }
        public static void Validate(Profile p,bool files) {
            p.Extension=Extension(p.Extension);
            if(string.IsNullOrWhiteSpace(p.Application)||!Path.IsPathRooted(p.Application)||!p.Application.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("请选择默认打开程序的 .exe 文件。");
            if(p.Application.IndexOfAny(new[]{'"','\r','\n','%'})>=0) throw new ArgumentException("应用路径不能含有引号、换行或百分号。");
            if(string.IsNullOrWhiteSpace(p.IconPath)||!Path.IsPathRooted(p.IconPath)||!new[]{".ico",".exe",".dll"}.Contains(Path.GetExtension(p.IconPath).ToLowerInvariant())) throw new ArgumentException("请选择 .ico 图标，或带有图标资源的 .exe / .dll 文件。");
            if(p.IconPath.IndexOfAny(new[]{'"','\r','\n','%'})>=0) throw new ArgumentException("图标路径不能含有引号、换行或百分号。");
            if(p.IconIndex < -100000 || p.IconIndex > 100000) throw new ArgumentException("图标索引超出范围。");
            if(files&&!File.Exists(p.Application)) throw new FileNotFoundException("找不到打开程序："+p.Application);
            if(files&&!File.Exists(p.IconPath)) throw new FileNotFoundException("找不到图标文件："+p.IconPath);
        }
        public static string Hash(byte[] bytes) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
        public static string Fingerprint(Profile p) { return Hash(Encoding.UTF8.GetBytes(p.Extension+"\n"+p.Application+"\n"+p.IconPath+"\n"+p.IconIndex)); }
    }
    public class Store {
        public readonly string Root;
        public List<BuiltinIcon> Builtins;
        public Library Data;
        public Store(string root) {
            Root=Path.GetFullPath(root); Directory.CreateDirectory(Root);
            using(var stream=typeof(Store).Assembly.GetManifestResourceStream("BuiltinCatalog")) using(var buffer=new MemoryStream()) {
                stream.CopyTo(buffer); Builtins=Json.Read<List<BuiltinIcon>>(buffer.ToArray());
            }
            foreach(var builtin in Builtins) {
                using(var stream=typeof(Store).Assembly.GetManifestResourceStream(builtin.Resource)) using(var buffer=new MemoryStream()) {
                    stream.CopyTo(buffer); builtin.IconPath=ManagedIconBytes(buffer.ToArray());
                }
            }
            string file=Path.Combine(Root,"library.json");
            if(File.Exists(file)) {
                try { Data=Json.Read<Library>(File.ReadAllBytes(file)); ValidateLibrary(Data); }
                catch(Exception e) { throw new IOException("配置仓库无法读取，已保留原文件。可检查 library.json.bak。\n"+e.Message,e); }
            } else { Data=new Library(); Save(); }
        }
        public static void ValidateLibrary(Library data) {
            if(data==null||data.Version!=1||data.Profiles==null||data.Revisions==null) throw new ArgumentException("不支持的仓库格式。");
            var extensions=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids=new HashSet<string>();
            foreach(var p in data.Profiles) {
                if(p==null||string.IsNullOrWhiteSpace(p.Id)||!Regex.IsMatch(p.Id,"^[a-fA-F0-9]{32}$")||!ids.Add(p.Id)) throw new ArgumentException("仓库中有无效或重复的配置 ID。");
                Rules.Validate(p,false);
                if(!extensions.Add(p.Extension)) throw new ArgumentException("仓库中存在重复后缀："+p.Extension);
            }
            foreach(var r in data.Revisions) if(r==null||string.IsNullOrWhiteSpace(r.Id)||string.IsNullOrWhiteSpace(r.Folder)||!Path.IsPathRooted(r.Folder)) throw new ArgumentException("仓库中有无效的历史记录。");
        }
        public void Save() { ValidateLibrary(Data); Json.Atomic(Path.Combine(Root,"library.json"),Data); }
        public string ManagedIcon(string path) {
            if(!path.EndsWith(".ico",StringComparison.OrdinalIgnoreCase)) return path;
            return ManagedIconBytes(File.ReadAllBytes(path));
        }
        private string ManagedIconBytes(byte[] bytes) {
            // Validate the ICO directory header before importing it.
            if(bytes.Length<22||bytes[0]!=0||bytes[1]!=0||bytes[2]!=1||bytes[3]!=0||(bytes[4]==0&&bytes[5]==0)) throw new ArgumentException("所选文件不是有效的 ICO 图标。");
            string folder=Path.Combine(Root,"assets"); Directory.CreateDirectory(folder);
            string dest=Path.Combine(folder,Rules.Hash(bytes)+".ico");
            if(!File.Exists(dest)) File.WriteAllBytes(dest,bytes);
            return dest;
        }
        public Profile Upsert(Profile input) {
            var p=Json.Copy(input); Rules.Validate(p,true);
            if(Data.Profiles.Any(x=>x.Extension==p.Extension&&x.Id!=p.Id)) throw new ArgumentException("仓库已有这个后缀，请从左侧选择它进行编辑。");
            p.IconPath=ManagedIcon(p.IconPath); if(p.IconPath.EndsWith(".ico",StringComparison.OrdinalIgnoreCase)) p.IconIndex=0;
            var old=Data.Profiles.FirstOrDefault(x=>x.Id==p.Id);
            if(old==null||Rules.Fingerprint(old)!=Rules.Fingerprint(p)) { p.UpdatedAt=DateTime.UtcNow.ToString("o"); p.ConfirmedExisting=false; }
            var before=Json.Copy(Data);
            try { Data.Profiles.RemoveAll(x=>x.Id==p.Id); Data.Profiles.Add(p); Save(); }
            catch { Data=before; throw; }
            return p;
        }
        public void Delete(string id) { var before=Json.Copy(Data); try { Data.Profiles.RemoveAll(x=>x.Id==id); Data.Revisions.RemoveAll(x=>x.ProfileId==id); Save(); } catch { Data=before; throw; } }
        public void Import(string file) {
            var incoming=Json.Read<Library>(File.ReadAllBytes(file)); ValidateLibrary(incoming);
            var before=Json.Copy(Data);
            try {
                foreach(var p in incoming.Profiles) {
                    var existing=Data.Profiles.FirstOrDefault(x=>x.Extension==p.Extension);
                    p.Id=existing==null?Guid.NewGuid().ToString("N"):existing.Id;
                    p.ConfirmedExisting=false; p.UpdatedAt=DateTime.UtcNow.ToString("o");
                    if(File.Exists(p.IconPath)) p.IconPath=ManagedIcon(p.IconPath);
                    Data.Profiles.RemoveAll(x=>x.Id==p.Id); Data.Profiles.Add(p);
                }
                Save();
            } catch { Data=before; throw; }
        }
        public Receipt ReadReceipt(Revision revision) {
            try { var r=Json.Read<Receipt>(File.ReadAllBytes(Path.Combine(revision.Folder,"receipt.json"))); return r.RevisionId==revision.Id?r:null; } catch { return null; }
        }
        public string Status(Profile p) {
            var lastCompleted=Data.Revisions.Where(r=>r.ProfileId==p.Id).Select(r=>new {Revision=r,Receipt=ReadReceipt(r)}).Where(x=>x.Receipt!=null).OrderByDescending(x=>x.Receipt.CompletedAt??x.Revision.CreatedAt).FirstOrDefault();
            if(lastCompleted!=null&&lastCompleted.Revision.Fingerprint==Rules.Fingerprint(p)) {
                var status=lastCompleted.Receipt.Status;
                return status=="applied"?"已执行":status=="restored"?"已还原":"执行失败";
            }
            var revisions=Data.Revisions.Where(r=>r.ProfileId==p.Id&&r.Fingerprint==Rules.Fingerprint(p)).OrderByDescending(r=>r.CreatedAt).ToList();
            if(revisions.Count>0) return "已生成";
            if(p.ConfirmedExisting) return "已有配置";
            return "已保存";
        }
        public string ScriptsRoot { get { return Path.Combine(Root,"scripts"); } }
        public Revision Generate(Profile p) { return GenerateMany(new[]{p},false).Single(); }
        public List<Revision> GenerateAll() { return GenerateMany(Data.Profiles.OrderBy(p=>p.Extension).ToArray(),true); }
        private List<Revision> GenerateMany(IEnumerable<Profile> profiles,bool all) {
            var selected=profiles.Select(p=>Json.Copy(p)).ToList();
            if(selected.Count==0) throw new ArgumentException("仓库为空，请先保存一个后缀配置。");
            foreach(var p in selected) Rules.Validate(p,true);
            string packageId=DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N");
            string folder=Path.Combine(ScriptsRoot,"history",packageId);
            var revisions=new List<Revision>(); var payloads=new List<Payload>();
            foreach(var p in selected) {
                var rev=new Revision{Id=Guid.NewGuid().ToString("N"),ProfileId=p.Id,Fingerprint=Rules.Fingerprint(p),CreatedAt=DateTime.UtcNow.ToString("o"),ScriptFolder=folder};
                rev.Folder=Path.Combine(folder,"items",rev.Id); revisions.Add(rev);
                bool bundle=Path.GetExtension(p.IconPath).Equals(".ico",StringComparison.OrdinalIgnoreCase);
                payloads.Add(new Payload{Extension=p.Extension,Application=p.Application,IconPath=p.IconPath,IconIndex=p.IconIndex,BundledIcon=bundle,IconData=bundle?Convert.ToBase64String(File.ReadAllBytes(p.IconPath)):null,RevisionId=rev.Id});
            }
            string template;
            using(var stream=typeof(Store).Assembly.GetManifestResourceStream("ApplyTemplate")) using(var reader=new StreamReader(stream,Encoding.UTF8)) template=reader.ReadToEnd();
            string script=template.Replace("@@PAYLOAD@@",Convert.ToBase64String(Json.Bytes(payloads))).Replace("@@PACKAGE@@",packageId);
            Directory.CreateDirectory(folder);
            foreach(var rev in revisions) Directory.CreateDirectory(rev.Folder);
            File.WriteAllText(Path.Combine(folder,"apply.ps1"),script,new UTF8Encoding(true));
            WriteLauncher(Path.Combine(folder,"apply.cmd"),"apply.ps1",false);
            WriteLauncher(Path.Combine(folder,"restore.cmd"),"apply.ps1",true);
            File.WriteAllText(Path.Combine(folder,"README.txt"),"Icon Controller\r\n"+string.Join("\r\n",selected.Select(p=>p.Extension+" → "+p.Application))+"\r\n\r\napply.cmd 应用全部配置；restore.cmd 按反序还原本版本已应用的配置。\r\n先预检全部配置，再逐项执行；失败会停止，已成功项保留，可运行还原入口。\r\nICO 已嵌入脚本。items 中保存各项回执及备份，请保留本目录。\r\n",new UTF8Encoding(true));
            Data.Revisions.AddRange(revisions);
            try { Save(); } catch { foreach(var rev in revisions) Data.Revisions.Remove(rev); throw; }
            string name=all?"apply-all":"apply";
            string target=Path.Combine(ScriptsRoot,name+".ps1");
            string temp=target+".tmp"; File.WriteAllText(temp,script,new UTF8Encoding(true));
            if(File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target);
            WriteLauncher(Path.Combine(ScriptsRoot,name+".cmd"),name+".ps1",false);
            WriteLauncher(Path.Combine(ScriptsRoot,all?"restore-all.cmd":"restore.cmd"),name+".ps1",true);
            return revisions;
        }
        private static void WriteLauncher(string path,string script,bool restore) {
            File.WriteAllText(path,"@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%~dp0"+script+"\""+(restore?" -Restore":"")+"\r\nset \"result=%errorlevel%\"\r\nif not \"%result%\"==\"0\" echo Operation failed. See the error above.\r\npause\r\nexit /b %result%\r\n",Encoding.ASCII);
        }
    }
}