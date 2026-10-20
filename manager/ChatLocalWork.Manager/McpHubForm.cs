using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatLocalWork.Manager;

internal sealed class McpHubForm : Form
{
    private sealed class Registry { [JsonPropertyName("version")] public int Version {get;set;}=1; [JsonPropertyName("servers")] public List<HubEntry> Servers {get;set;}=[]; }
    private sealed class HubEntry
    {
        [JsonPropertyName("id")] public string Id {get;set;}="";
        [JsonPropertyName("name")] public string Name {get;set;}="";
        [JsonPropertyName("enabled")] public bool Enabled {get;set;}
        [JsonPropertyName("transport")] public string Transport {get;set;}="streamableHttp";
        [JsonPropertyName("url")] public string? Url {get;set;}
        [JsonPropertyName("allowedTools")] public List<string> AllowedTools {get;set;}=[];
        public override string ToString() => $"{(Enabled ? "[ON] " : "[OFF] ")}{Name} ({Id})";
    }
    private readonly string _file;
    private readonly ListBox _list = new() { Dock=DockStyle.Fill, IntegralHeight=false };
    private readonly TextBox _id=new() {Width=310};
    private readonly TextBox _name=new() {Width=310};
    private readonly TextBox _url=new() {Width=310};
    private readonly TextBox _command=new() {Width=310};
    private readonly TextBox _cwd=new() {Width=310};
    private readonly TextBox _args=new() {Multiline=true,ScrollBars=ScrollBars.Vertical};
    private readonly TextBox _envNames=new() {Multiline=true,ScrollBars=ScrollBars.Vertical};
    private readonly McpStdioProfiles _profiles;
    private readonly string _workspaceRoot;
    private readonly ComboBox _transport=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly TextBox _tools=new() {Width=310,Multiline=true,Height=100,ScrollBars=ScrollBars.Vertical};
    private readonly CheckBox _enabled=new() {Text="활성화",AutoSize=true};
    private Registry _registry=new();
    private static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=true,DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
    public McpHubForm(string dataRoot, string workspaceRoot)
    {
        _file=Path.Combine(dataRoot,"config","mcp-servers.json");
        _profiles=new McpStdioProfiles(dataRoot);
        _workspaceRoot=workspaceRoot;
        Text="MCP 서버 관리"; StartPosition=FormStartPosition.CenterParent;
        MinimumSize=new Size(890,670);Size=new Size(980,770);AutoScaleMode=AutoScaleMode.Dpi;
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(14),ColumnCount=2,RowCount=2};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,42));root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,72));
        var edit=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=11,AutoScroll=true};
        edit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100)); edit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        void Add(int row,string label,Control control,int height) {edit.RowStyles.Add(new RowStyle(SizeType.Absolute,height));edit.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);control.Dock=DockStyle.Fill;edit.Controls.Add(control,1,row);}
        _transport.Items.AddRange(["HTTP MCP", "Windows REA", "Windows stdio MCP"]); _transport.SelectedIndex=0;
        _transport.SelectedIndexChanged+=(_,_)=>UpdateTransportFields();
        Add(0,"서버 ID",_id,40);Add(1,"이름",_name,40);Add(2,"유형",_transport,40);Add(3,"주소",_url,48);
        Add(4,"실행 파일",_command,48);Add(5,"작업 폴더",_cwd,48);Add(6,"인수 (줄별)",_args,72);
        Add(7,"환경 변수 이름",_envNames,65);Add(8,"허용 도구",_tools,98);Add(9,"상태",_enabled,40);
        edit.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        _list.SelectedIndexChanged+=(_,_)=>{
            if(_list.SelectedItem is not HubEntry e)return;
            _id.Text=e.Id;_name.Text=e.Name;_url.Text=e.Url??"";
            _transport.SelectedIndex=e.Transport=="windowsRea"?1:e.Transport=="windowsStdio"?2:0;
            _enabled.Checked=e.Enabled;_tools.Text=string.Join(Environment.NewLine,e.AllowedTools);
            var profile=_profiles.Find(e.Id);
            _command.Text=profile?.Command??"";_cwd.Text=profile?.Cwd??_workspaceRoot;
            _args.Lines=profile?.Args.ToArray()??[];
            _envNames.Lines=profile?.EnvNames.ToArray()??[];
            UpdateTransportFields();
        };
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true,AutoScroll=true};
        var save=new Button{Text="추가 / 저장",AutoSize=true};var delete=new Button{Text="선택 삭제",AutoSize=true};var reload=new Button{Text="다시 읽기",AutoSize=true};var rea=new Button{Text="REA 자동 설정",AutoSize=true};var codex=new Button{Text="Codex 설정 가져오기",AutoSize=true};var inspect=new Button{Text="도구 조회...",AutoSize=true};var verify=new Button{Text="연결 확인...",AutoSize=true};
        buttons.Controls.AddRange([save,delete,reload,rea,codex,inspect,verify]);
        inspect.Click+=async (_,_)=>await InspectStdioToolsAsync(inspect);
        verify.Click+=async (_,_)=>await VerifyStdioConnectionAsync(verify);
        codex.Click+=(_,_)=>ImportCodex();
        rea.Click+=(_,_)=>SetReaDefaults();
        save.Click+=(_,_)=>SaveEntry();delete.Click+=(_,_)=>DeleteEntry();reload.Click+=(_,_)=>Reload();
        root.Controls.Add(_list,0,0);root.Controls.Add(edit,1,0);root.Controls.Add(buttons,0,1);root.SetColumnSpan(buttons,2);
        Controls.Add(root);_cwd.Text=_workspaceRoot;UpdateTransportFields();Reload();
    }
    private void UpdateTransportFields()
    {
        var stdio=_transport.SelectedIndex==2;
        _url.Enabled=_transport.SelectedIndex==0;
        foreach(var control in new Control[]{_command,_cwd,_args,_envNames})control.Enabled=stdio;
    }
    private void Reload() { try { _registry=File.Exists(_file)?JsonSerializer.Deserialize<Registry>(File.ReadAllText(_file))??new():new(); RefreshList(); } catch(Exception ex){MessageBox.Show(this,ex.Message,"Registry 읽기 실패",MessageBoxButtons.OK,MessageBoxIcon.Error);} }
    private void RefreshList(){_list.Items.Clear();foreach(var e in _registry.Servers)_list.Items.Add(e);}
    private void Persist(){Directory.CreateDirectory(Path.GetDirectoryName(_file)!);var tmp=_file+".tmp";File.WriteAllText(tmp,JsonSerializer.Serialize(_registry,JsonOptions));File.Move(tmp,_file,true);RefreshList();}
    private void SetReaDefaults()
    {
        _id.Text="rea";_name.Text="Reverse Engineer Anything";_transport.SelectedIndex=1;_url.Text="";
        _tools.Lines=["open_binary","list_documents","current_document","list_segments","list_procedures","list_strings","list_names","search_procedures","search_strings","read_bytes","procedure_info","procedure_pseudo_code","procedure_assembly","procedure_callers","procedure_callees","read_function_instructions","inspect_native_instruction","inspect_native_data_type","inspect_native_load_image","address_to_file_offset","procedure_references"];
        _enabled.Checked=true;
    }
    private void ImportCodex()
    {
        var file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex","config.toml");
        if(!File.Exists(file)){MessageBox.Show(this,"Codex 설정 파일이 없습니다: "+file);return;}
        try
        {
            var found=CodexMcpImporter.Read(File.ReadAllText(file));
            var imported=0;
            foreach(var item in found)
            {
                var id=item.Name.ToLowerInvariant();
                if(_registry.Servers.Any(x=>x.Id==id))continue;
                // Imported stdio servers cannot be launched by the HTTP hub yet.
                // Recognize the existing REA transport without copying arbitrary env.
                if(id=="rea" && item.Command is not null && item.Args.Any(x=>x.Contains("rea-agents",StringComparison.OrdinalIgnoreCase)))
                {
                    _registry.Servers.Add(new HubEntry {Id="rea",Name="Reverse Engineer Anything",Transport="windowsRea",Enabled=false,AllowedTools=[]});
                    imported++;
                }
                else if(item.Command is not null && item.Url is null)
                {
                    // Import metadata only. No arguments or environment values are copied.
                    // The user must explicitly configure and approve the host-only profile.
                    _registry.Servers.Add(new HubEntry {Id=id,Name=item.Name,Transport="windowsStdio",Enabled=false,AllowedTools=[]});
                    imported++;
                }
                else if(item.Url is not null && Uri.TryCreate(item.Url,UriKind.Absolute,out var uri)
                    && uri.Scheme=="http" && new[]{"localhost","127.0.0.1","host.docker.internal"}.Contains(uri.Host)
                    && uri.UserInfo.Length==0 && uri.Query.Length==0 && uri.Fragment.Length==0)
                {
                    _registry.Servers.Add(new HubEntry {Id=id,Name=item.Name,Transport="streamableHttp",Url=item.Url,Enabled=false,AllowedTools=[]});
                    imported++;
                }
            }
            if(imported>0)Persist();
            MessageBox.Show(this,$"Codex 설정 {found.Count}개 발견, {imported}개를 비활성 상태로 가져왔습니다.\n\n실행 환경과 허용 도구를 확인한 뒤 직접 활성화해 주세요. stdio 항목은 실행 명령을 가져오지 않았으며 수동 승인 후 사용할 수 있습니다.","Codex 설정 가져오기");
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Codex 설정 분석 오류");}
    }
    private async Task VerifyStdioConnectionAsync(Button button)
    {
        if(_transport.SelectedIndex!=2) {MessageBox.Show(this,"Windows stdio MCP 유형에서 사용하세요.");return;}
        var command=_command.Text.Trim();var cwd=_cwd.Text.Trim();
        var args=_args.Lines.Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray();
        var envNames=_envNames.Lines.Select(x=>x.Trim()).Where(x=>x.Length>0).Distinct().ToArray();
        var allowed=_tools.Lines.Select(x=>x.Trim()).Where(x=>x.Length>0).Distinct().ToArray();
        var root=Path.GetFullPath(_workspaceRoot).TrimEnd(Path.DirectorySeparatorChar);
        if(!Directory.Exists(cwd) || !(Path.GetFullPath(cwd).Equals(root,StringComparison.OrdinalIgnoreCase)
           || Path.GetFullPath(cwd).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
        {MessageBox.Show(this,"작업 폴더는 Workspace 내부여야 합니다.");return;}

        var detail=$"실행 파일: {command}\n인수: {string.Join(" ",args)}\n작업 폴더: {cwd}\n환경 변수 이름: {string.Join(", ",envNames)}\n허용 도구: {(allowed.Length==0?"(없음)":string.Join(", ",allowed))}\n\n연결 확인을 위해 initialize와 tools/list만 실행합니다. 실제 도구 호출이나 승인 저장은 하지 않습니다. 실행하시겠습니까?";
        if(MessageBox.Show(this,detail,"MCP 연결 확인",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return;

        button.Enabled=false;
        try
        {
            var result=await McpStdioInspector.VerifyAllowedToolsAsync(command,args,cwd,envNames,allowed,CancellationToken.None);
            if(result.MissingAllowedTools.Count>0)
            {
                var missing=string.Join(Environment.NewLine,result.MissingAllowedTools.Take(30));
                if(result.MissingAllowedTools.Count>30)missing+=$"{Environment.NewLine}... 외 {result.MissingAllowedTools.Count-30}개";
                MessageBox.Show(this,$"MCP 서버 연결에는 성공했지만 현재 허용 도구 중 서버에 없는 항목이 있습니다.\n\n조회 도구: {result.DiscoveredTools.Count}개\n누락 도구: {result.MissingAllowedTools.Count}개\n\n{missing}","MCP 연결 확인",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                return;
            }

            var allowedSummary=allowed.Length==0
                ?"현재 허용 도구는 지정하지 않았습니다."
                :$"현재 허용 도구 {allowed.Length}개가 모두 서버에 존재합니다.";
            MessageBox.Show(this,$"MCP 서버 연결에 성공했습니다.\n\n조회 도구: {result.DiscoveredTools.Count}개\n{allowedSummary}\n\n실제 tools/call은 수행하지 않았습니다.","MCP 연결 확인",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        catch(Exception ex)
        {
            MessageBox.Show(this,ex.Message,"MCP 연결 확인 실패",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
        finally {button.Enabled=true;}
    }

    private async Task InspectStdioToolsAsync(Button button)
    {
        if(_transport.SelectedIndex!=2) {MessageBox.Show(this,"Windows stdio MCP 유형에서 사용하세요.");return;}
        var command=_command.Text.Trim();var cwd=_cwd.Text.Trim();
        var args=_args.Lines.Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray();
        var envNames=_envNames.Lines.Select(x=>x.Trim()).Where(x=>x.Length>0).Distinct().ToArray();
        var root=Path.GetFullPath(_workspaceRoot).TrimEnd(Path.DirectorySeparatorChar);
        if(!Directory.Exists(cwd) || !(Path.GetFullPath(cwd).Equals(root,StringComparison.OrdinalIgnoreCase)
           || Path.GetFullPath(cwd).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
        {MessageBox.Show(this,"작업 폴더는 Workspace 내부여야 합니다.");return;}
        var detail=$"실행 파일: {command}\n인수: {string.Join(" ",args)}\n작업 폴더: {cwd}\n환경 변수 이름: {string.Join(", ",envNames)}\n\n도구 목록 조회를 위해 프로그램을 한 번 실행합니다. 실행 파일 자체의 부작용은 보장할 수 없습니다. 승인하시겠습니까?";
        if(MessageBox.Show(this,detail,"MCP 실행 승인 (도구 조회 전용)",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
        button.Enabled=false;
        try
        {
            var tools=await McpStdioInspector.ListToolsAsync(command,args,cwd,envNames,CancellationToken.None);
            using var dialog=new Form {Text="허용할 MCP 도구 선택",Width=590,Height=610,MinimumSize=new Size(480,450),StartPosition=FormStartPosition.CenterParent,AutoScaleMode=AutoScaleMode.Dpi};
            var list=new CheckedListBox {Dock=DockStyle.Fill,CheckOnClick=true,HorizontalScrollbar=true};
            foreach(var name in tools)list.Items.Add(name,false);
            var explanation=new Label {Text=$"조회 도구 {tools.Count}개 — 허용할 도구만 선택하세요. 기본 선택은 0개입니다.",Dock=DockStyle.Top,Height=50};
            var add=new Button {Text="선택 도구 추가",AutoSize=true,DialogResult=DialogResult.OK};
            var cancel=new Button {Text="취소",AutoSize=true,DialogResult=DialogResult.Cancel};
            var bottom=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=55,AutoScroll=true};
            bottom.Controls.AddRange([add,cancel]);
            dialog.Controls.Add(list);dialog.Controls.Add(bottom);dialog.Controls.Add(explanation);
            dialog.AcceptButton=add;dialog.CancelButton=cancel;
            if(dialog.ShowDialog(this)==DialogResult.OK)
            {
                var selected=list.CheckedItems.Cast<string>().ToArray();
                if(selected.Length>0)_tools.Lines=_tools.Lines.Concat(selected).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
            }
        }
        catch(Exception ex) {MessageBox.Show(this,ex.Message,"MCP 도구 조회 실패",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        finally {button.Enabled=true;}
    }
    private void SaveEntry()
    {
        var id=_id.Text.Trim();if(!System.Text.RegularExpressions.Regex.IsMatch(id,@"^[a-z][a-z0-9_-]{0,47}$")){MessageBox.Show(this,"ID는 소문자로 시작하고 소문자, 숫자, _ - 만 허용됩니다.");return;}
        var windowsRea = _transport.SelectedIndex==1;
        var stdio = _transport.SelectedIndex==2;
        Uri? uri=null;
        if(windowsRea && id!="rea") { MessageBox.Show(this,"Windows REA 유형의 ID는 rea여야 합니다.");return; }
        if(!windowsRea && !stdio && (!Uri.TryCreate(_url.Text.Trim(),UriKind.Absolute,out uri)||uri.Scheme!="http"||!new[]{"127.0.0.1","localhost","host.docker.internal"}.Contains(uri.Host)||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0)){MessageBox.Show(this,"주소는 localhost/127.0.0.1/host.docker.internal HTTP 주소만 가능합니다.");return;}
        var tools=_tools.Lines.Select(x=>x.Trim()).Where(x=>x.Length>0).Distinct().ToList();
        if(_enabled.Checked&&tools.Count==0){MessageBox.Show(this,"활성화하려면 허용할 도구 이름을 하나 이상 지정하세요.");return;}
        if(stdio)
        {
            if(id=="rea") {MessageBox.Show(this,"기존 REA ID는 전용 연결을 사용합니다.");return;}
            var command=_command.Text.Trim();var cwd=_cwd.Text.Trim();
            var arguments=_args.Lines.Where(x=>!string.IsNullOrEmpty(x)).ToList();
            var envNames=_envNames.Lines.Select(x=>x.Trim()).Where(x=>x.Length>0).Distinct().ToList();
            var blocked=new[]{"powershell.exe","pwsh.exe","cmd.exe","wscript.exe","cscript.exe","mshta.exe","rundll32.exe","regsvr32.exe"};
            if(!Path.IsPathFullyQualified(command)||!command.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(command)||blocked.Contains(Path.GetFileName(command).ToLowerInvariant()))
            {MessageBox.Show(this,"실행 파일은 존재하는 Windows 절대 경로의 허용된 .exe여야 합니다.");return;}
            var fullCwd=Path.GetFullPath(cwd);
            var root=Path.GetFullPath(_workspaceRoot).TrimEnd(Path.DirectorySeparatorChar);
            if(!Directory.Exists(fullCwd)||!(fullCwd.Equals(root,StringComparison.OrdinalIgnoreCase)||fullCwd.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
            {MessageBox.Show(this,"작업 폴더는 Workspace 내부여야 합니다.");return;}
            if(arguments.Count>64||string.Join("",arguments).Length>16000||tools.Count>100||envNames.Count>40||envNames.Any(x=>!System.Text.RegularExpressions.Regex.IsMatch(x,@"^[A-Za-z_][A-Za-z0-9_]{0,80}$")))
            {MessageBox.Show(this,"실행 인수, 허용 도구 또는 환경 변수 설정이 올바르지 않습니다.");return;}
            var detail=$"서버: {id}\n실행: {command}\n인수: {string.Join(" ",arguments)}\n작업 폴더: {fullCwd}\n환경 변수: {string.Join(", ",envNames)}\n허용 도구: {string.Join(", ",tools)}\n\n이 설정을 사용하면 MCP 서버 프로그램이 Windows에서 실행됩니다. 승인하시겠습니까?";
            if(MessageBox.Show(this,detail,"Windows stdio MCP 실행 승인",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
            _profiles.Upsert(new McpStdioProfiles.Profile{Id=id,Approved=true,Command=command,Args=arguments,Cwd=fullCwd,EnvNames=envNames,AllowedTools=tools});
        }
        var item=_registry.Servers.FirstOrDefault(x=>x.Id==id);if(item==null){item=new HubEntry{Id=id};_registry.Servers.Add(item);}item.Name=_name.Text.Trim();item.Transport=windowsRea?"windowsRea":stdio?"windowsStdio":"streamableHttp";item.Url=windowsRea||stdio?null:uri!.ToString();item.Enabled=_enabled.Checked;item.AllowedTools=tools;Persist();
    }
    private void DeleteEntry(){if(_list.SelectedItem is not HubEntry e)return;if(MessageBox.Show(this,$"{e.Name} 서버를 삭제하시겠습니까?","확인",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;_registry.Servers.Remove(e);Persist();_profiles.Delete(e.Id);}
}
