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
    private readonly ComboBox _transport=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly TextBox _tools=new() {Width=310,Multiline=true,Height=100,ScrollBars=ScrollBars.Vertical};
    private readonly CheckBox _enabled=new() {Text="활성화",AutoSize=true};
    private Registry _registry=new();
    private static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=true,DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
    public McpHubForm(string workspaceRoot)
    {
        _file=Path.Combine(workspaceRoot,".chatlocalwork","mcp-servers.json");
        Text="MCP 서버 관리"; StartPosition=FormStartPosition.CenterParent;
        MinimumSize=new Size(750,520);Size=new Size(860,600);AutoScaleMode=AutoScaleMode.Dpi;
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(14),ColumnCount=2,RowCount=2};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,42));root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        var edit=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=7,AutoScroll=true};
        edit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100)); edit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        void Add(int row,string label,Control control,int height) {edit.RowStyles.Add(new RowStyle(SizeType.Absolute,height));edit.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);control.Dock=DockStyle.Fill;edit.Controls.Add(control,1,row);}
        _transport.Items.AddRange(["HTTP MCP", "Windows REA"]); _transport.SelectedIndex=0;
        _transport.SelectedIndexChanged+=(_,_)=>{_url.Enabled=_transport.SelectedIndex==0;};
        Add(0,"서버 ID",_id,42);Add(1,"이름",_name,42);Add(2,"유형",_transport,42);Add(3,"주소",_url,62);Add(4,"허용 도구",_tools,125);Add(5,"상태",_enabled,42);
        edit.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        _list.SelectedIndexChanged+=(_,_)=>{if(_list.SelectedItem is not HubEntry e)return;_id.Text=e.Id;_name.Text=e.Name;_url.Text=e.Url??"";_transport.SelectedIndex=e.Transport=="windowsRea"?1:0;_enabled.Checked=e.Enabled;_tools.Text=string.Join(Environment.NewLine,e.AllowedTools);};
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true,AutoScroll=true};
        var save=new Button{Text="추가 / 저장",AutoSize=true};var delete=new Button{Text="선택 삭제",AutoSize=true};var reload=new Button{Text="다시 읽기",AutoSize=true};var rea=new Button{Text="REA 자동 설정",AutoSize=true};
        buttons.Controls.AddRange([save,delete,reload,rea]);
        rea.Click+=(_,_)=>SetReaDefaults();
        save.Click+=(_,_)=>SaveEntry();delete.Click+=(_,_)=>DeleteEntry();reload.Click+=(_,_)=>Reload();
        root.Controls.Add(_list,0,0);root.Controls.Add(edit,1,0);root.Controls.Add(buttons,0,1);root.SetColumnSpan(buttons,2);
        Controls.Add(root);Reload();
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
    private void SaveEntry()
    {
        var id=_id.Text.Trim();if(!System.Text.RegularExpressions.Regex.IsMatch(id,@"^[a-z][a-z0-9_-]{0,47}$")){MessageBox.Show(this,"ID는 소문자로 시작하고 소문자, 숫자, _ - 만 허용됩니다.");return;}
        var windowsRea = _transport.SelectedIndex==1;
        Uri? uri=null;
        if(windowsRea && id!="rea") { MessageBox.Show(this,"Windows REA 유형의 ID는 rea여야 합니다.");return; }
        if(!windowsRea && (!Uri.TryCreate(_url.Text.Trim(),UriKind.Absolute,out uri)||uri.Scheme!="http"||!new[]{"127.0.0.1","localhost","host.docker.internal"}.Contains(uri.Host)||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0)){MessageBox.Show(this,"주소는 localhost/127.0.0.1/host.docker.internal HTTP 주소만 가능합니다.");return;}
        var tools=_tools.Lines.Select(x=>x.Trim()).Where(x=>x.Length>0).Distinct().ToList();
        if(_enabled.Checked&&tools.Count==0){MessageBox.Show(this,"활성화하려면 허용할 도구 이름을 하나 이상 지정하세요.");return;}
        var item=_registry.Servers.FirstOrDefault(x=>x.Id==id);if(item==null){item=new HubEntry{Id=id};_registry.Servers.Add(item);}item.Name=_name.Text.Trim();item.Transport=windowsRea?"windowsRea":"streamableHttp";item.Url=windowsRea?null:uri!.ToString();item.Enabled=_enabled.Checked;item.AllowedTools=tools;Persist();
    }
    private void DeleteEntry(){if(_list.SelectedItem is not HubEntry e)return;if(MessageBox.Show(this,$"{e.Name} 서버를 삭제하시겠습니까?","확인",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;_registry.Servers.Remove(e);Persist();}
}
