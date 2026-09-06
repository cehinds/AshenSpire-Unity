using System.Text;
using System.Text.Json;
using AshenSpire.Domain;
using AshenSpire.Editor;

var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
if(args.Length==2 && args[0]=="--validate-campaign")
{
    var strict=new JsonSerializerOptions{IncludeFields=true,UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow};
    CampaignAuthoringValidation.Validate(JsonSerializer.Deserialize<CampaignDefinition>(File.ReadAllText(args[1]),strict)!,Path.Combine(root,"Unity/Assets/AshenSpire/Resources/Art"));
    Console.WriteLine("Campaign content: complete schema, references and sprite validation passed");return;
}
var workspace=Path.Combine(root,"Builds/AuthoringChecks",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(workspace);
var source=Path.Combine(workspace,"campaign.json");var backups=Path.Combine(workspace,"Backups");
var canonical=File.ReadAllText(Path.Combine(root,"GameContent/Unity/campaign.json"));
var options=new JsonSerializerOptions{IncludeFields=true};
CampaignDefinition Content()=>JsonSerializer.Deserialize<CampaignDefinition>(canonical,options)!;
void Validate(string json)=>CampaignAuthoringValidation.Validate(JsonSerializer.Deserialize<CampaignDefinition>(json,options)!,Path.Combine(root,"Unity/Assets/AshenSpire/Resources/Art"));
var checks=new List<string>();
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);checks.Add(name);Console.WriteLine("PASS: "+name);}
void Reject<T>(Action action,string name) where T:Exception{try{action();}catch(T){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
File.WriteAllText(source,canonical);var initial=File.ReadAllBytes(source);var hash=CampaignContentFile.Hash(source);
var bad=Content();bad.Heroes[0].Deck[0]="missing-card";
Reject<ArgumentException>(()=>CampaignContentFile.Save(source,hash,JsonSerializer.Serialize(bad,options),backups,Validate,()=>{}),"dangling reference rejected before source replacement");
Check(File.ReadAllBytes(source).SequenceEqual(initial)&&!Directory.Exists(backups),"invalid definition preserves source and creates no backup");
bad=Content();bad.Equipment[0].Art="missing-sprite";
Reject<ArgumentException>(()=>CampaignContentFile.Save(source,hash,JsonSerializer.Serialize(bad,options),backups,Validate,()=>{}),"missing equipment sprite rejected before source replacement");
Check(File.ReadAllBytes(source).SequenceEqual(initial),"missing art leaves source byte-identical");
bad=Content();bad.Heroes[0].Name="";
Reject<ArgumentException>(()=>Validate(JsonSerializer.Serialize(bad,options)),"empty hero display name rejected");
bad=Content();bad.Encounters[0].Name=" ";
Reject<ArgumentException>(()=>Validate(JsonSerializer.Serialize(bad,options)),"empty encounter display name rejected");
bad=Content();bad.Equipment[0].Name=null;
Reject<ArgumentException>(()=>Validate(JsonSerializer.Serialize(bad,options)),"empty equipment display name rejected");
var changed=Content();changed.Audio.Volume=.2f;var candidate=JsonSerializer.Serialize(changed,options);
var external=canonical+"\n ";File.WriteAllText(source,external);
Reject<IOException>(()=>CampaignContentFile.Save(source,hash,candidate,backups,Validate,()=>{}),"stale draft cannot overwrite external source changes");
Check(File.ReadAllText(source)==external,"stale save preserves the external bytes");
File.WriteAllBytes(source,new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(canonical.Replace("\n","\r\n"))).ToArray());
var exactOld=File.ReadAllBytes(source);var imported=0;
var result=CampaignContentFile.Save(source,CampaignContentFile.Hash(source),candidate,backups,Validate,()=>imported++);
Check(result.Imported&&imported==1&&File.ReadAllText(source)==candidate,"valid save replaces source and imports once");
Check(File.ReadAllBytes(result.BackupPath).SequenceEqual(exactOld),"backup preserves exact BOM and line ending bytes");
Check(result.SourceHash==CampaignContentFile.Hash(source),"save receipt identifies the committed source bytes");
changed.Audio.Volume=.4f;var next=JsonSerializer.Serialize(changed,options);
var failedImport=CampaignContentFile.Save(source,result.SourceHash,next,backups,Validate,()=>throw new IOException("simulated import failure"));
Check(!failedImport.Imported&&failedImport.ImportError=="simulated import failure"&&File.ReadAllText(source)==next,"import failure reports source saved accurately");
Check(File.ReadAllText(failedImport.BackupPath)==candidate,"import failure still retains the prior source backup");
var recovery=Path.Combine(workspace,"Drafts/campaign-draft.json");
var checkpoint=new CampaignDraftCheckpoint{DraftJson=candidate,SavedJson=canonical,SourceHash=hash};
var json=JsonSerializer.Serialize(checkpoint,options);CampaignContentFile.Checkpoint(recovery,json);CampaignContentFile.Checkpoint(recovery,json);
Check(Directory.GetFiles(Path.GetDirectoryName(recovery)!).Length==1,"identical checkpoint does not create redundant backups");
var restored=JsonSerializer.Deserialize<CampaignDraftCheckpoint>(File.ReadAllText(recovery),options)!;
Check(restored.DraftJson==candidate&&restored.SavedJson==canonical&&restored.SourceHash==hash,"recovery preserves draft and original disk baseline");
Reject<IOException>(()=>CampaignContentFile.Save(source,restored.SourceHash,restored.DraftJson,backups,Validate,()=>{}),"recovered stale draft retains conflict detection");
CampaignContentFile.Checkpoint(recovery,"new checkpoint");
var old=Directory.GetFiles(Path.GetDirectoryName(recovery)!,"*.previous-*.json");
Check(old.Length==1&&File.ReadAllText(old[0])==json,"new checkpoint preserves the prior draft bytes");
Check(Directory.GetFiles(workspace,"*.tmp",SearchOption.AllDirectories).Length==0,"transactions leave no temporary files");
var report=args.Length>0?Path.GetFullPath(args[0]):Path.Combine(workspace,"checks.json");Directory.CreateDirectory(Path.GetDirectoryName(report)!);
File.WriteAllText(report,JsonSerializer.Serialize(new{passed=checks.Count,checks,workspace},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Authoring files: {checks.Count} checks passed");
