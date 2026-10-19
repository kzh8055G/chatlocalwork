using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatLocalWork.Manager;

// This file is deliberately outside the config directory Docker can read.
internal sealed class McpStdioProfiles
{
    internal sealed class Profile
    {
        [JsonPropertyName("id")] public string Id {get;set;}="";
        [JsonPropertyName("approved")] public bool Approved {get;set;}
        [JsonPropertyName("command")] public string Command {get;set;}="";
        [JsonPropertyName("args")] public List<string> Args {get;set;}=[];
        [JsonPropertyName("cwd")] public string Cwd {get;set;}="";
        [JsonPropertyName("envNames")] public List<string> EnvNames {get;set;}=[];
        [JsonPropertyName("allowedTools")] public List<string> AllowedTools {get;set;}=[];
    }
    private sealed class Document
    {
        [JsonPropertyName("version")] public int Version {get;set;}=1;
        [JsonPropertyName("servers")] public List<Profile> Servers {get;set;}=[];
    }
    private readonly string _file;
    private readonly Document _document;
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true};
    internal McpStdioProfiles(string dataRoot)
    {
        _file=Path.Combine(dataRoot,"private","mcp-stdio-profiles.json");
        _document=File.Exists(_file)?JsonSerializer.Deserialize<Document>(File.ReadAllText(_file))??new():new();
        if(_document.Version!=1 || _document.Servers.Count>32) throw new InvalidDataException("Invalid Windows stdio MCP profiles");
    }
    internal Profile? Find(string id)=>_document.Servers.FirstOrDefault(x=>x.Id==id);
    internal void Upsert(Profile profile)
    {
        var existing=Find(profile.Id);
        if(existing is not null)_document.Servers.Remove(existing);
        _document.Servers.Add(profile);
        Save();
    }
    internal void Delete(string id) {if(_document.Servers.RemoveAll(x=>x.Id==id)>0)Save();}
    private void Save()
    {
        var dir=Path.GetDirectoryName(_file)!;
        Directory.CreateDirectory(dir);
        var tmp=_file+".tmp";
        File.WriteAllText(tmp,JsonSerializer.Serialize(_document,Options));
        File.Move(tmp,_file,true);
    }
}
