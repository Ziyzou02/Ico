using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace IconController {
    public static class Program {
        [STAThread] public static int Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");
            using(var mutex=new Mutex(false,"Local\\IconController-"+Rules.Hash(Encoding.UTF8.GetBytes(root)).Substring(0,16))) {
                bool acquired=false;
                try {
                    try { acquired=mutex.WaitOne(0); } catch(AbandonedMutexException) { acquired=true; }
                    if(!acquired) { MessageBox.Show("Icon Controller 已在运行，请使用已打开的窗口。","Icon Controller"); return 0; }
                    Application.Run(new MainForm(new Store(root))); return 0;
                } catch(Exception e) { MessageBox.Show(e.Message,"无法启动 Icon Controller",MessageBoxButtons.OK,MessageBoxIcon.Error); return 1; }
                finally { if(acquired) mutex.ReleaseMutex(); }
            }
        }
    }
    public class MainForm : Form {
        readonly Store store;
        readonly Color ink=Color.FromArgb(28,38,56), muted=Color.FromArgb(109,120,139), blue=Color.FromArgb(48,88,222), line=Color.FromArgb(222,227,236);
        ListView library, history;
        TextBox search, appPath, iconPath, note;
        ComboBox extension, apps;
        NumericUpDown index;
        PictureBox preview;
        Label count, detailTitle, previewName, feedback, historyTitle;
        Button saveButton, generateButton;
        Profile editing;
        bool loading, dirty;
        System.Windows.Forms.Timer refreshTimer;
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern uint ExtractIconEx(string file,int index,IntPtr[] large,IntPtr[] small,uint count);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr instance,string path,uint type,int width,int height,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wparam,string text);
        public MainForm(Store store) {
            SuspendLayout();
            this.store=store; Text="Icon Controller · 文件图标控制器"; Font=new Font("Microsoft YaHei UI",9F); ForeColor=ink;
            Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            BackColor=Color.FromArgb(245,247,251); MinimumSize=new Size(1040,730);
            Size=new Size(1140,800); StartPosition=FormStartPosition.CenterScreen;
            var outer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(18),RowCount=1};
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,290)); outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); Controls.Add(outer);
            outer.Controls.Add(BuildSidebar(),0,0); outer.Controls.Add(BuildMain(),1,0);
            AutoScaleDimensions=new SizeF(96F,96F); AutoScaleMode=AutoScaleMode.Dpi;
            ResumeLayout(true);
            LoadExtensions(); LoadApps(); RefreshLibrary(null);
            if(store.Data.Profiles.Count>0) LoadProfile(store.Data.Profiles.OrderBy(x=>x.Extension).First()); else NewProfile();
            refreshTimer=new System.Windows.Forms.Timer{Interval=4000}; refreshTimer.Tick+=(s,e)=>RefreshStatuses(); refreshTimer.Start();
            FormClosing+=(s,e)=>{ if(!DiscardChanges()) e.Cancel=true; };
            Shown+=(s,e)=>{ if(Height>Screen.FromControl(this).WorkingArea.Height) Height=Screen.FromControl(this).WorkingArea.Height; };
        }
        Label Label(string text,float size,Color color,bool bold=false) { return new Label{Text=text,AutoSize=true,ForeColor=color,Font=new Font(Font.FontFamily,size,bold?FontStyle.Bold:FontStyle.Regular),Margin=new Padding(0,0,0,6)}; }
        Button Button(string text,EventHandler action,bool primary=false) {
            var b=new Button{Text=text,AutoSize=false,Height=36,Width=110,FlatStyle=FlatStyle.Flat,BackColor=primary?blue:Color.White,ForeColor=primary?Color.White:ink,Cursor=Cursors.Hand,Margin=new Padding(0,0,8,0)};
            b.FlatAppearance.BorderSize=primary?0:1; b.FlatAppearance.BorderColor=line; b.Click+=action; return b;
        }
        TextBox TextField() { return new TextBox{Dock=DockStyle.Fill,BorderStyle=BorderStyle.FixedSingle,Margin=new Padding(0,3,0,0)}; }
        Control BuildSidebar() {
            var panel=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(16),ColumnCount=1,RowCount=8,Margin=new Padding(0,0,16,0)};
            foreach(var h in new[]{34,30,39}) panel.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent,100)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute,44)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute,44)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
            panel.Controls.Add(Label("◈  ICON CONTROLLER",12,ink,true),0,0);
            count=Label("配置仓库",9,muted); panel.Controls.Add(count,0,1);
            search=TextField(); search.AccessibleName="搜索后缀或应用"; search.HandleCreated+=(s,e)=>SendMessage(search.Handle,0x1501,IntPtr.Zero,"搜索后缀或应用…"); search.TextChanged+=(s,e)=>RefreshLibrary(editing==null?null:editing.Id); panel.Controls.Add(search,0,2);
            library=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,HideSelection=false,MultiSelect=false,BorderStyle=BorderStyle.None,HeaderStyle=ColumnHeaderStyle.Nonclickable};
            library.Columns.Add("后缀",64); library.Columns.Add("应用",95); library.Columns.Add("状态",68);
            library.Resize+=(s,e)=>{int w=Math.Max(100,library.ClientSize.Width-6);library.Columns[0].Width=(int)(w*.27);library.Columns[1].Width=(int)(w*.38);library.Columns[2].Width=(int)(w*.35);};
            library.SmallImageList=new ImageList{ImageSize=new Size(1,32)};
            library.SelectedIndexChanged+=(s,e)=>{
                if(loading||library.SelectedItems.Count==0) return;
                var p=(Profile)library.SelectedItems[0].Tag;
                if(editing!=null&&p.Id==editing.Id) return;
                if(!DiscardChanges()) { RefreshLibrary(editing==null?null:editing.Id); return; }
                LoadProfile(p);
            };
            panel.Controls.Add(library,0,3);
            var row=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false}; row.Controls.Add(Button("＋ 新建后缀",(s,e)=>{if(DiscardChanges()) NewProfile();},true)); row.Controls.Add(Button("移出仓库",DeleteProfile)); panel.Controls.Add(row,0,4);
            var transfer=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false}; transfer.Controls.Add(Button("导入配置",Import)); transfer.Controls.Add(Button("导出配置",Export)); panel.Controls.Add(transfer,0,5);
            var repo=Button("打开统一脚本目录",(s,e)=>{Directory.CreateDirectory(store.ScriptsRoot);OpenFolder(store.ScriptsRoot);}); repo.Width=236; panel.Controls.Add(repo,0,6);
            var all=Button("一键生成全部配置",GenerateAll,true);all.Width=236;panel.Controls.Add(all,0,7);
            return panel;
        }
        Control BuildMain() {
            var main=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Margin=new Padding(0)};
            main.RowStyles.Add(new RowStyle(SizeType.Absolute,78)); main.RowStyles.Add(new RowStyle(SizeType.Absolute,380)); main.RowStyles.Add(new RowStyle(SizeType.Absolute,49)); main.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); main.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var head=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};
            detailTitle=Label("文件图标控制器",22,ink,true); head.Controls.Add(detailTitle); head.Controls.Add(Label("选后缀、定应用、换图标。配置保存在本地，随时再次生成。",9,muted)); main.Controls.Add(head,0,0);
            var card=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(20,16,20,12),ColumnCount=2,RowCount=8,Margin=new Padding(0,0,0,12)};
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,156));
            foreach(var h in new[]{25,36,25,36,35,25,36,80}) card.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
            card.Controls.Add(Label("01   文件后缀",10,ink,true),0,0);
            extension=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDown,AutoCompleteMode=AutoCompleteMode.SuggestAppend,AutoCompleteSource=AutoCompleteSource.ListItems,Margin=new Padding(0,2,10,3),AccessibleName="文件后缀"};
            extension.TextChanged+=Changed; card.Controls.Add(extension,0,1);
            card.Controls.Add(Label("02   默认打开应用",10,ink,true),0,2);
            appPath=TextField(); appPath.AccessibleName="应用路径"; appPath.TextChanged+=Changed;
            card.Controls.Add(PathRow(appPath,"选择 EXE",(s,e)=>BrowseApplication()),0,3);
            apps=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(0,1,10,3),AccessibleName="常用应用"};
            apps.SelectedIndexChanged+=(s,e)=>{if(!loading&&apps.SelectedItem is AppItem){var a=(AppItem)apps.SelectedItem;if(a.Path!=null) appPath.Text=a.Path;}};
            card.Controls.Add(apps,0,4);
            card.Controls.Add(Label("03   图标地址",10,ink,true),0,5);
            iconPath=TextField(); iconPath.AccessibleName="图标地址"; iconPath.TextChanged+=(s,e)=>{Changed(s,e);UpdatePreview();};
            card.Controls.Add(PathRow(iconPath,"选择图标",(s,e)=>BrowseIcon()),0,6);
            var extra=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=new Padding(0)};
            extra.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,112)); extra.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            extra.RowStyles.Add(new RowStyle(SizeType.Absolute,25)); extra.RowStyles.Add(new RowStyle(SizeType.Absolute,30));
            extra.Controls.Add(Label("资源索引",9,muted),0,0); extra.Controls.Add(Label("备注（可选）",9,muted),1,0);
            index=new NumericUpDown{Minimum=-100000,Maximum=100000,Width=95,Margin=new Padding(0),AccessibleName="图标资源索引"}; index.ValueChanged+=(s,e)=>{Changed(s,e);UpdatePreview();}; extra.Controls.Add(index,0,1);
            note=TextField(); note.AccessibleName="配置备注"; note.TextChanged+=Changed; extra.Controls.Add(note,1,1); card.Controls.Add(extra,0,7);
            var visual=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(18,18,0,0)};
            visual.Controls.Add(Label("软件自带图标库",9,muted));
            var builtins=new ComboBox{Width=124,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName="软件自带图标库",Margin=new Padding(0,3,0,0)};
            builtins.Items.Add("选择内置图标…");foreach(var builtin in store.Builtins)builtins.Items.Add(builtin);builtins.SelectedIndex=0;
            builtins.SelectedIndexChanged+=(s,e)=>{var item=builtins.SelectedItem as BuiltinIcon;if(item!=null){iconPath.Text=item.IconPath;index.Value=0;if(string.IsNullOrWhiteSpace(extension.Text))extension.Text=item.Extension;}};
            visual.Controls.Add(builtins);
            preview=new PictureBox{Size=new Size(112,112),SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(245,247,251),Margin=new Padding(0,10,0,14)}; visual.Controls.Add(preview);
            previewName=new Label{Text="选择一个图标",Width=122,Height=92,ForeColor=muted,AutoEllipsis=true}; visual.Controls.Add(previewName);
            card.Controls.Add(visual,1,0); card.SetRowSpan(visual,8); main.Controls.Add(card,0,1);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false}; saveButton=Button("保存配置",Save); generateButton=Button("生成应用脚本",Generate,true); generateButton.Width=155;
            actions.Controls.Add(generateButton);actions.Controls.Add(saveButton);actions.Controls.Add(Button("刷新状态",(s,e)=>{RefreshStatuses();SetFeedback("已读取脚本执行结果。",false);})); main.Controls.Add(actions,0,2);
            feedback=new Label{Dock=DockStyle.Fill,ForeColor=muted,AutoEllipsis=true,Text="生成后双击 apply.cmd 执行；应用本身不会直接修改文件关联。",Margin=new Padding(0,4,0,0)}; main.Controls.Add(feedback,0,3);
            var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,BackColor=Color.White,Padding=new Padding(14,10,14,8),Margin=new Padding(0)};
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute,29));bottom.RowStyles.Add(new RowStyle(SizeType.Percent,100));bottom.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
            historyTitle=Label("脚本历史",11,ink,true); bottom.Controls.Add(historyTitle,0,0);
            history=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,HideSelection=false,MultiSelect=false,BorderStyle=BorderStyle.None};
            history.Columns.Add("生成时间",168);history.Columns.Add("执行状态",102);history.Columns.Add("版本",120);
            history.Resize+=(s,e)=>{int w=Math.Max(100,history.ClientSize.Width-6);history.Columns[0].Width=(int)(w*.43);history.Columns[1].Width=(int)(w*.25);history.Columns[2].Width=(int)(w*.32);};
            history.DoubleClick+=(s,e)=>OpenSelectedRevision(false); bottom.Controls.Add(history,0,1);
            var histActions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false}; histActions.Controls.Add(Button("打开脚本目录",(s,e)=>OpenSelectedRevision(false))); histActions.Controls.Add(Button("查看脚本",(s,e)=>OpenSelectedRevision(true))); histActions.Controls.Add(Button("查看执行结果",(s,e)=>ShowResult())); bottom.Controls.Add(histActions,0,2);
            main.Controls.Add(bottom,0,4); return main;
        }
        Control PathRow(TextBox field,string caption,EventHandler browse) {
            var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,10,0)};
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,92));
            row.Controls.Add(field,0,0); var button=Button(caption,browse); button.Width=84;button.Height=28;button.Margin=new Padding(8,1,0,0);row.Controls.Add(button,1,0);return row;
        }
        void Changed(object s,EventArgs e) { if(loading)return;dirty=true;saveButton.Text="保存配置 *"; }
        bool DiscardChanges() { return !dirty||MessageBox.Show(this,"尚有未保存的编辑，放弃这些编辑？","未保存的配置",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes; }
        void SetFeedback(string message,bool error) { feedback.ForeColor=error?Color.FromArgb(173,61,48):muted; feedback.Text=message; }
        void Error(Exception e) { SetFeedback(e.Message,true); MessageBox.Show(this,e.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
        void NewProfile() { LoadProfile(new Profile()); extension.Focus(); SetFeedback("填写配置后点击“生成应用脚本”，会同时保存到仓库。",false); }
        void LoadProfile(Profile p) {
            loading=true; editing=Json.Copy(p); extension.Text=p.Extension; appPath.Text=p.Application; iconPath.Text=p.IconPath; index.Value=p.IconIndex;note.Text=p.Note??"";
            detailTitle.Text=string.IsNullOrWhiteSpace(p.Extension)?"新建后缀配置":p.Extension+"  文件配置"; dirty=false;saveButton.Text="保存配置";loading=false;
            UpdatePreview();RefreshLibrary(p.Id);RefreshHistory();
        }
        Profile ReadEditor() { var p=Json.Copy(editing);p.Extension=extension.Text;p.Application=appPath.Text.Trim().Trim('"');p.IconPath=iconPath.Text.Trim().Trim('"');p.IconIndex=(int)index.Value;p.Note=note.Text.Trim();return p; }
        Profile Persist() { var p=store.Upsert(ReadEditor());LoadProfile(p);return p; }
        void Save(object s,EventArgs e) { try {var p=Persist();SetFeedback("已保存 "+p.Extension+"；文件关联尚未更改。",false);}catch(Exception ex){Error(ex);} }
        void Generate(object s,EventArgs e) {
            try {var p=Persist();var r=store.Generate(p);RefreshLibrary(p.Id);RefreshHistory();SetFeedback("脚本已生成。双击 apply.cmd 应用，restore.cmd 可还原。",false);OpenFolder(store.ScriptsRoot);}catch(Exception ex){Error(ex);}
        }
        void GenerateAll(object s,EventArgs e) {
            try {
                if(dirty) Persist();
                var revisions=store.GenerateAll();RefreshLibrary(editing==null?null:editing.Id);RefreshHistory();
                SetFeedback("已生成 "+revisions.Count+" 个后缀的统一脚本。双击 apply-all.cmd 执行。",false);OpenFolder(store.ScriptsRoot);
            } catch(Exception ex) { Error(ex); }
        }
        void RefreshLibrary(string selectedId) {
            bool wasLoading=loading;loading=true;library.BeginUpdate();library.Items.Clear();
            string filter=search.Text.Trim();
            foreach(var p in store.Data.Profiles.OrderBy(x=>x.Extension)) {
                if(filter.Length>0&&(p.Extension+" "+p.Application+" "+p.Note).IndexOf(filter,StringComparison.OrdinalIgnoreCase)<0)continue;
                var item=new ListViewItem(new[]{p.Extension,Path.GetFileNameWithoutExtension(p.Application),store.Status(p)}){Tag=p,ToolTipText=p.Note+"\n"+p.Application};
                library.Items.Add(item);item.Selected=p.Id==selectedId;
            }
            library.EndUpdate();loading=wasLoading;count.Text="配置仓库  ·  "+store.Data.Profiles.Count+" 个后缀";
        }
        void RefreshHistory() {
            string selected=history.SelectedItems.Count>0?((Revision)history.SelectedItems[0].Tag).Id:null;
            history.BeginUpdate();history.Items.Clear();
            if(editing!=null)foreach(var r in store.Data.Revisions.Where(x=>x.ProfileId==editing.Id).OrderByDescending(x=>x.CreatedAt)) {
                var receipt=store.ReadReceipt(r);string status=receipt==null?"等待执行":receipt.Status=="applied"?"已执行":receipt.Status=="restored"?"已还原":"执行失败";
                DateTime dt;string time=DateTime.TryParse(r.CreatedAt,out dt)?dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"):r.CreatedAt;
                var item=new ListViewItem(new[]{time,status,r.Id.Substring(0,Math.Min(8,r.Id.Length))}){Tag=r};history.Items.Add(item);item.Selected=selected==r.Id;
            }
            if(history.SelectedItems.Count==0&&history.Items.Count>0)history.Items[0].Selected=true;
            history.EndUpdate();historyTitle.Text="脚本历史  ·  "+history.Items.Count+" 个版本";
        }
        void RefreshStatuses() {foreach(ListViewItem row in library.Items)row.SubItems[2].Text=store.Status((Profile)row.Tag);RefreshHistory();}
        Revision SelectedRevision() {return history.SelectedItems.Count>0?(Revision)history.SelectedItems[0].Tag:null;}
        void OpenSelectedRevision(bool script) {
            var r=SelectedRevision();if(r==null){SetFeedback("请先生成或选择一个脚本版本。",false);return;}
            try {if(script)Process.Start(new ProcessStartInfo("notepad.exe","\""+Path.Combine(r.ScriptFolder??r.Folder,"apply.ps1")+"\""){UseShellExecute=true});else OpenFolder(r.ScriptFolder??r.Folder);}catch(Exception e){Error(e);}
        }
        void ShowResult() {var r=SelectedRevision();if(r==null)return;var result=store.ReadReceipt(r);MessageBox.Show(this,result==null?"此版本还没有执行结果。请在文件资源管理器中双击 apply.cmd。":result.Status+"\n\n"+result.Message+"\n\n备份："+result.BackupFolder,"脚本执行结果",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        void OpenFolder(string folder) {try{if(!Directory.Exists(folder))throw new DirectoryNotFoundException("目录已移动或不存在："+folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception e){Error(e);}}
        void DeleteProfile(object s,EventArgs e) {
            if(editing==null||!store.Data.Profiles.Any(x=>x.Id==editing.Id))return;
            if(MessageBox.Show(this,"将 "+editing.Extension+" 移出配置仓库？\nWindows 当前关联、图标文件和已生成的脚本都会保留。","移出仓库",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            try{store.Delete(editing.Id);dirty=false;NewProfile();}catch(Exception ex){Error(ex);}
        }
        void Import(object s,EventArgs e) {
            using(var d=new OpenFileDialog{Filter="配置仓库 (*.json)|*.json",Title="导入后缀配置"})if(d.ShowDialog(this)==DialogResult.OK){
                if(!DiscardChanges())return;
                if(MessageBox.Show(this,"导入配置会合并到仓库；相同后缀的配置会被替换。继续？","导入配置",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
                try{store.Import(d.FileName);dirty=false;if(store.Data.Profiles.Count>0)LoadProfile(store.Data.Profiles[0]);else NewProfile();SetFeedback("已导入配置。生成脚本前会检查程序和图标地址。",false);}catch(Exception ex){Error(ex);}
            }
        }
        void Export(object s,EventArgs e) {using(var d=new SaveFileDialog{Filter="配置仓库 (*.json)|*.json",FileName="icon-controller-library.json",Title="导出配置（包含本地路径）"})if(d.ShowDialog(this)==DialogResult.OK){try{var copy=new Library{Profiles=Json.Copy(store.Data.Profiles)};Json.Atomic(d.FileName,copy);SetFeedback("已导出配置。图标素材保存在仓库 assets 目录。",false);}catch(Exception ex){Error(ex);}}}
        void BrowseApplication(){using(var d=new OpenFileDialog{Filter="Windows 应用 (*.exe)|*.exe",Title="选择默认打开应用"})if(d.ShowDialog(this)==DialogResult.OK)appPath.Text=d.FileName;}
        void BrowseIcon(){using(var d=new OpenFileDialog{Filter="图标文件 (*.ico;*.exe;*.dll)|*.ico;*.exe;*.dll|ICO 图标 (*.ico)|*.ico",Title="选择图标"})if(d.ShowDialog(this)==DialogResult.OK){iconPath.Text=d.FileName;index.Value=0;}}
        void UpdatePreview() {
            if(preview==null||index==null)return;
            Image image=null;string path=iconPath.Text.Trim().Trim('"');
            try {
                if(File.Exists(path)) {
                    if(path.EndsWith(".ico",StringComparison.OrdinalIgnoreCase)){
                        IntPtr handle=LoadImage(IntPtr.Zero,path,1,128,128,0x10);
                        if(handle!=IntPtr.Zero){try{using(var copy=(Icon)Icon.FromHandle(handle).Clone())image=copy.ToBitmap();}finally{DestroyIcon(handle);}}
                    }
                    else if(new[]{".exe",".dll"}.Contains(Path.GetExtension(path).ToLowerInvariant())) {
                        var large=new IntPtr[1];var small=new IntPtr[1];
                        try {if(ExtractIconEx(path,(int)index.Value,large,small,1)>0){var handle=large[0]!=IntPtr.Zero?large[0]:small[0];if(handle!=IntPtr.Zero){using(var copy=(Icon)Icon.FromHandle(handle).Clone())image=copy.ToBitmap();}}}
                        finally {if(large[0]!=IntPtr.Zero)DestroyIcon(large[0]);if(small[0]!=IntPtr.Zero)DestroyIcon(small[0]);}
                    }
                }
            }catch{}
            var old=preview.Image;preview.Image=image;if(old!=null)old.Dispose();
            string displayName=path.StartsWith(Path.Combine(store.Root,"assets"),StringComparison.OrdinalIgnoreCase)?"仓库图标 · ICO":Path.GetFileName(path);
            previewName.Text=image==null?"选择 ICO 文件，或应用中的图标资源。":displayName+"\n\n"+(path.EndsWith(".ico",StringComparison.OrdinalIgnoreCase)?"ICO 将随脚本打包":"保留此资源文件路径");
            index.Enabled=!path.EndsWith(".ico",StringComparison.OrdinalIgnoreCase);
        }
        void LoadExtensions(){try{extension.Items.AddRange(Registry.ClassesRoot.GetSubKeyNames().Where(x=>x.StartsWith(".")&&x.Length<34).OrderBy(x=>x).Cast<object>().ToArray());}catch{extension.Items.AddRange(new object[]{".typ",".jl",".py",".md",".txt"});}}
        class AppItem {public string Name;public string Path;public override string ToString(){return Name;}}
        void LoadApps(){
            var found=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var p in store.Data.Profiles)found[p.Application]=Path.GetFileNameWithoutExtension(p.Application);
            foreach(var hive in new[]{Registry.CurrentUser,Registry.LocalMachine}){
                try{using(var root=hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths")){if(root!=null)foreach(string name in root.GetSubKeyNames()){using(var k=root.OpenSubKey(name)){string path=Convert.ToString(k.GetValue("")).Trim('"');if(path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)&&File.Exists(path))found[path]=Path.GetFileNameWithoutExtension(path);}}}}catch{}
            }
            string notepad=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"notepad.exe");if(File.Exists(notepad))found[notepad]="Notepad";
            apps.Items.Add(new AppItem{Name="从常用应用中选择…"});foreach(var pair in found.OrderBy(x=>x.Value))apps.Items.Add(new AppItem{Name=pair.Value+"  ·  "+pair.Key,Path=pair.Key});apps.SelectedIndex=0;
        }
        protected override void Dispose(bool disposing){if(disposing){if(refreshTimer!=null)refreshTimer.Dispose();if(preview!=null&&preview.Image!=null)preview.Image.Dispose();}base.Dispose(disposing);}
    }
}
