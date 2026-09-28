// Reproduce the actual compiled Web copy failure with complete authored saves,
// then exercise all slots/backups under PlayerPrefs' documented 1 MiB limit.
using System.IO.Compression;
using System.Text;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

internal static class SaveCapacityChecks
{
    public static void Run(string root, OriginalContentCatalog catalog, Action<bool,string> check)
    {
        JObject Rules(string name) => JObject.Parse(File.ReadAllText(Path.Combine(root,"GameContent/Unity/Original",name+".json")));
        var mechanics=Rules("mechanics");var progression=new AttributeProgression(Rules("progression"));
        var creator=new CreationModel(catalog,"reaver","leanStandard",progression);
        var kit=(string)catalog.Table("equipment.startingKits").First(row=>(string)row["classId"]=="reaver"&&(bool?)row["baseline"]==true)["id"];
        var player=new OriginalCharacterBuilder(catalog,progression,mechanics).Build(creator,kit);
        player["runId"]="capacity-fixture";player["name"]="Ember 🌋 雪";player["profileMeta"]=new OriginalProfile(catalog).Snapshot();
        var game=OriginalGameSession.Start(catalog,Rules("event-choices"),mechanics,player,1);
        var snapshot=game.Snapshot();
        {
            var quota=new QuotaStorage();var plain=new OriginalSaveSlots(quota,"web","0.0.25.0");
            check(plain.Save(0,snapshot,0)&&plain.Save(0,snapshot,1),"one authored plain slot and its backup fit the Web budget");
            check(!plain.Copy(0,1),"baseline reproduces the compiled copy failure at the Web budget");
            check(plain.List()[1].State==OriginalSaveSlotState.Empty,"failed plain copy leaves the target empty");
        }
        {
            var quota=new QuotaStorage();var encoded=new OriginalCompressedSaveStorage(quota);
            var slots=new OriginalSaveSlots(encoded,"web","0.0.25.0");
            check(slots.Save(0,snapshot,0)&&slots.Save(0,snapshot,1),"encoded first slot and backup save successfully");
            check(slots.Copy(0,1)&&slots.Copy(0,2),"both additional authored slots copy within the Web budget");
            for(var slot=0;slot<3;slot++)check(slots.Save(slot,snapshot,2)&&slots.Save(slot,snapshot,3),"slot and backup update under Web quota: "+slot);
            var profile=slots.LoadProfile(catalog,out _);
            for(var n=0;n<20;n++){var run=game.RunPlayer;run["runId"]="capacity-result-"+n;slots.RecordResult(profile,run,false);}
            check(slots.LoadProfile(catalog,out _).ResultArchive().Count==20,"full result archive persists alongside all three slots and backups");
            Console.WriteLine("Complete three-slot Web store: "+quota.Bytes+" bytes");
            check(quota.Bytes<700000,"three complete slots, backups and full profile retain Web storage headroom");
            var reloaded=new OriginalSaveSlots(new OriginalCompressedSaveStorage(quota),"web","0.0.25.0");
            for(var slot=0;slot<3;slot++){
                var restored=reloaded.Load(slot,value=>OriginalGameSession.Restore(value),out _,out var recovered);
                check(!recovered&&JToken.DeepEquals(restored,snapshot),"new storage instance restores exact authored snapshot and Unicode: "+slot);
            }
            quota.Write(slots.SlotKey(0),"ASZ1:not-base64");
            var backup=reloaded.Load(0,value=>OriginalGameSession.Restore(value),out _,out var usedBackup);
            check(usedBackup&&JToken.DeepEquals(backup,snapshot),"malformed encoded primary falls back to a valid encoded backup");
            check(reloaded.Save(0,snapshot,4)&&encoded.Read(slots.SlotKey(0)+".corrupt")=="ASZ1:not-base64","malformed encoded value is preserved during recovery");
            var prior=quota.Read(slots.SlotKey(1));quota.Limit=quota.Bytes;
            var larger=(JObject)snapshot.DeepClone();larger["capacityFixture"]=string.Concat(Enumerable.Range(0,2000).Select(n=>n.ToString("X")));
            check(!reloaded.Save(1,larger,5),"real quota exhaustion still reports failure after encoding");
            quota.Limit=1024*1024;
            check(JToken.DeepEquals(reloaded.Load(1,value=>OriginalGameSession.Restore(value),out _,out _),snapshot),"quota failure keeps the previous loadable checkpoint");
            check(reloaded.Save(1,snapshot,6),"encoded journal retries when space is available");
        }
        {
            var quota=new QuotaStorage();var encoded=new OriginalCompressedSaveStorage(quota);
            var slots=new OriginalSaveSlots(encoded,"upgrade","0.0.25.0");
            var legacy=OriginalSaveJournal.Envelope(snapshot);
            quota.Write(slots.LegacyRunKey,legacy);quota.Write(slots.LegacyRunKey+".backup",legacy);
            check(slots.CompactLegacyRecords(value=>OriginalGameSession.Restore(value)),"historical records compact before migration with verified recovery copies");
            check(slots.MigrateLegacy()==OriginalLegacyMigration.Migrated,"old plain single-run saves migrate into compact slots");
            check(slots.Copy(0,1)&&slots.Copy(0,2),"migrated player can fill all three slots without deleting legacy saves");
            for(var slot=0;slot<3;slot++)check(slots.Save(slot,snapshot,1),"migrated slot can keep its own backup: "+slot);
            check(encoded.Read(slots.LegacyRunKey)==legacy&&encoded.Read(slots.LegacyRunKey+".backup")==legacy,"original logical legacy envelope bytes remain exactly unchanged");
            check(slots.SaveProfile(new OriginalProfile(catalog)),"profile still saves with all migrated slots and retained legacy records");
            Console.WriteLine("Migrated three-slot store including retained legacy pair: "+quota.Bytes+" bytes");
        }
        {
            var memory=new OriginalMemorySaveStorage();var encoded=new OriginalCompressedSaveStorage(memory);
            memory.Write("plain","{\"saved\":true}");check(encoded.Read("plain")==memory.Read("plain"),"existing plain values are read without a rewrite");
            var tiny="ASZ1:"+Convert.ToBase64String(new byte[]{1,2,3});memory.Write("broken",tiny);
            check(encoded.Read("broken")==tiny,"invalid gzip bytes remain available for quarantine");
            using var packed=new MemoryStream();
            using(var gzip=new GZipStream(packed,CompressionLevel.Optimal,true)){
                var block=new byte[8192];for(var n=0;n<4097;n++)gzip.Write(block,0,block.Length);
            }
            var oversized="ASZ1:"+Convert.ToBase64String(packed.ToArray());memory.Write("oversized",oversized);
            check(encoded.Read("oversized")==oversized,"oversized decompression is rejected without replacing the stored record");
            encoded.Delete("plain");check(memory.Read("plain")==null,"encoded storage preserves explicit delete semantics");
        }
        void ValidateEnvelope(string value)
        { new OriginalSaveJournal("candidate",_=>value,(_,__)=>{},()=>{}).Load(saved=>OriginalGameSession.Restore(saved),out _); }
        foreach(var mode in new[]{"write-before","write-after","persistent-after","flush","read-back"})
        {
            var memory=new OriginalMemorySaveStorage();var faulty=new ThrowingStorage(memory,"historical");var encoded=new OriginalCompressedSaveStorage(faulty);
            var original=OriginalSaveJournal.Envelope(snapshot);memory.Write("historical",original);faulty.Arm(mode);
            check(!encoded.TryCompact("historical",ValidateEnvelope),"interrupted historical compaction reports failure: "+mode);
            faulty.Arm(null);
            check(encoded.Read("historical")==original||encoded.Read("historical.encoding-backup")==original,"interrupted compaction retains a complete recoverable envelope: "+mode);
            check(encoded.TryCompact("historical",ValidateEnvelope)&&encoded.Read("historical")==original&&memory.Read("historical.encoding-backup")==null,"compaction retry restores exact data and removes only its temporary copy: "+mode);
        }
        {
            var memory=new OriginalMemorySaveStorage();var faulty=new ThrowingStorage(memory,"historical.encoding-backup");var encoded=new OriginalCompressedSaveStorage(faulty);
            var original=OriginalSaveJournal.Envelope(snapshot);memory.Write("historical",original);faulty.Arm("write-after");
            check(!encoded.TryCompact("historical",ValidateEnvelope)&&memory.Read("historical")==original,"partial temporary write never touches the original historical record");
            faulty.Arm(null);check(encoded.TryCompact("historical",ValidateEnvelope)&&encoded.Read("historical")==original,"invalid temporary copy can be replaced using the valid original");
            memory.Write("damaged","keep damaged bytes");check(!encoded.TryCompact("damaged",ValidateEnvelope)&&memory.Read("damaged")=="keep damaged bytes","unreadable historical records are not compacted");
            var other=(JObject)snapshot.DeepClone();other["capacityFixture"]="concurrent record";var otherEnvelope=OriginalSaveJournal.Envelope(other);
            memory.Write("conflict",otherEnvelope);encoded.Write("conflict.encoding-backup",original);
            check(!encoded.TryCompact("conflict",ValidateEnvelope)&&encoded.Read("conflict")==otherEnvelope&&encoded.Read("conflict.encoding-backup")==original,"conflicting valid records are both preserved without choosing a winner");
        }
    }
    private sealed class QuotaStorage : IOriginalSaveStorage
    {
        private readonly OriginalMemorySaveStorage _memory=new OriginalMemorySaveStorage();
        public int Limit=1024*1024;
        private static int Size(string key,string value)=>Encoding.UTF8.GetByteCount(key)+Encoding.UTF8.GetByteCount(value??"");
        public int Bytes=>_memory.Values.Sum(pair=>Size(pair.Key,pair.Value));
        public string Read(string key)=>_memory.Read(key);
        public void Write(string key,string value){var previous=_memory.Read(key);var next=Bytes-(previous==null?0:Size(key,previous))+Size(key,value);if(next>Limit)throw new IOException("Fixture Web storage quota exceeded");_memory.Write(key,value);}
        public void Delete(string key)=>_memory.Delete(key);
        public void Flush()=>_memory.Flush();
    }
}
