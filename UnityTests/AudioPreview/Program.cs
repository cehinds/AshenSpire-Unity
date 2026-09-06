using System.Text;
using System.Text.Json;
using AshenSpire.Domain;
// Run from the repository root. Export reference PCM clips using the player's
// generator; master volume is intentionally omitted for separate auditioning.
var content=JsonSerializer.Deserialize<CampaignDefinition>(File.ReadAllText("GameContent/Unity/campaign.json"),new JsonSerializerOptions{IncludeFields=true})!;
Directory.CreateDirectory("Published/AudioEvidence");
foreach(var cue in content.Feedback.Cues)
{
    if(cue.Id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_'))
        throw new ArgumentException("Audio preview ID must contain only letters, digits, hyphens or underscores: "+cue.Id);
    var samples=FeedbackSound.Synthesize(cue);
    using var writer=new BinaryWriter(File.Create("Published/AudioEvidence/"+cue.Id+".wav"));
    void Four(string id)=>writer.Write(Encoding.ASCII.GetBytes(id));
    Four("RIFF");writer.Write(36+samples.Length*2);Four("WAVE");Four("fmt ");writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(22050);writer.Write(44100);writer.Write((short)2);writer.Write((short)16);Four("data");writer.Write(samples.Length*2);
    foreach(var sample in samples)writer.Write((short)Math.Round(sample*short.MaxValue));
    Console.WriteLine(cue.Id+": "+samples.Length+" samples; reference WAV, no master volume applied.");
}
