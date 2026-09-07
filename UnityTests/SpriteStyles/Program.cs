using System;using System.IO;using System.Linq;using Newtonsoft.Json.Linq;using AshenSpire.Presentation;
var folder=AppContext.BaseDirectory;
var repository=args.Length>0?Path.GetFullPath(args[0]):Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
var catalog=new OriginalSpriteCatalog(JObject.Parse(File.ReadAllText(Path.Combine(folder,"sprite-styles.json"))));
var oracle=JObject.Parse(File.ReadAllText(Path.Combine(folder,"source-reference.json")));
int checks=0;void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
foreach(JObject row in oracle["choices"]){var plan=catalog.Resolve((string)row["classId"],(JObject)row["customization"],(string)row["armour"]);Check((string)plan["style"]==(string)row["style"],"Style "+row);if((string)plan["style"]=="animated")Check((string)plan["poseClass"]==(string)row["poseClass"],"Outfit "+row);}
foreach(JObject row in oracle["geometry"]){var plan=new JObject{["poseClass"]=row["poseClass"],["tint"]=row["tint"]};var actual=catalog.FrameRect(plan,(string)row["pose"],(double)row["width"],(double)row["height"]);for(var i=0;i<4;i++)Check(Math.Abs(actual[i]-(double)row["rect"][i])<.000001,"Frame registration "+row);}
var playback=new OriginalSpritePlayback();var basePlan=catalog.Resolve("reaver",new JObject{["tint"]="gold"});foreach(var row in oracle["rotations"])Check(playback.Resolve(catalog,basePlan,(string)row["id"],"attack")== (string)row["pose"],"Per-figure rotation");
Check(catalog.Frame("reaver","missing","gold")==null,"Missing pose does not replace held pose");
Check((string)catalog.Resolve(null,new JObject())["style"]=="glyph","Missing class falls back to the original glyph renderer");
Check(catalog.Resolve("reaver",new JObject{["spriteStyle"]="rendered",["tint"]="gold"})["resource"].ToString()!=basePlan["resource"].ToString(),"Painted and animated assets are distinct");
Console.WriteLine("Original sprite style checks passed: "+checks);
var appearance=JObject.Parse(File.ReadAllText(Path.Combine(folder,"appearance-options.json")));
Check(JToken.DeepEquals(appearance["originalStyles"],new JArray(catalog.Styles.Select(row=>row["id"]))),"Renderer and appearance identities share original style IDs");
var receipts=JObject.Parse(File.ReadAllText(Path.Combine(folder,"asset-receipts.json")));
foreach(var receipt in receipts["files"]){var texture=Path.Combine(repository,"Unity/Assets/AshenSpire/Resources/Art/Styles",(string)receipt["file"]);Check(File.Exists(texture),"Imported texture exists: "+texture);var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(texture))).ToLowerInvariant();Check(hash==(string)receipt["sha256"],"Imported texture equals verified source conversion: "+texture);}
Console.WriteLine("Integrated source/assets/appearance checks passed: "+checks);
File.WriteAllText(Path.Combine(folder,"checks.json"),new JObject{["passed"]=true,["checks"]=checks,["choices"]=oracle["choices"].Count(),["geometry"]=oracle["geometry"].Count(),["scope"]="Actual original rendering route, pose geometry and per-figure attack rotation; no Unity rendered-pixel assertion."}.ToString());

