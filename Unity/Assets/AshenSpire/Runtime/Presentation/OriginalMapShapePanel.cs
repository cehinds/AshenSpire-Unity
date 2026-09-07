// OriginalMapShapePanel.cs — owner-facing caps/weights for Custom Climb.
// Mount inside the existing custom foldout. Controls edit only the setup draft;
// OriginalMapShape validates and samples with isolated seeds, never the live run.
// Labels/limits/sample count come from Original/custom-run-options/mapShape.
// Invalid settings disable Begin via callback and explain the exact refusal.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public static class OriginalMapShapePanel
    {
        public static string Problem(OriginalContentCatalog catalog, JObject setup, JObject options)
        {
            try { OriginalMapShape.ResolveAll((JObject)catalog.Data()["mapConfigs"],setup["custom"]?["mapShape"],options["mapShape"]?["limits"] as JObject); return null; }
            catch (ArgumentException error) { return error.Message; }
        }
        public static void Render(VisualElement parent, OriginalContentCatalog catalog, JObject setup, JObject options, Action report, Action<bool> validityChanged)
        {
            var authored = options["mapShape"] as JObject ?? throw new ArgumentException("Import authored map-shape options.");
            var configs = (JObject)catalog.Data()["mapConfigs"]; var acts = configs.Properties().OrderBy(p => int.Parse(p.Name)).ToArray();
            var baseline = (JObject)acts[0].Value; var limits = (JObject)authored["limits"]; var custom = (JObject)setup["custom"];
            var maximumFloors = acts.Max(a => (int)a.Value["floors"]); var minimumFloors = acts.Max(a => OriginalMapShape.MinimumFloors((JObject)a.Value));
            var maximumColumns = acts.Max(a => (int)a.Value["columns"]); var seedCount = (int)authored["estimateSeeds"];
            var group = new Foldout { text = (string)authored["label"], name = "native-map-shape", value = false }; parent.Add(group);
            group.Q<Toggle>().name = "native-map-shape-toggle";
            var summary = new Label { name = "native-map-shape-readout" }; summary.AddToClassList("caption"); group.Add(summary);
            var problem = new Label { name = "native-map-shape-problem" }; problem.AddToClassList("caption"); group.Add(problem);
            var controls = new List<SliderInt>();
            var samples = new Dictionary<string,JObject>(StringComparer.Ordinal);
            var floors = Field("native-map-floors",(string)authored["floorsLabel"],minimumFloors,maximumFloors,(int?)custom["mapShape"]?["floors"] ?? maximumFloors);
            var columns = Field("native-map-columns",(string)authored["columnsLabel"],(int)limits["minColumns"],maximumColumns,(int?)custom["mapShape"]?["columns"] ?? maximumColumns);
            var weights = new Dictionary<string,SliderInt>();
            var note = new Label((string)authored["note"]); note.AddToClassList("caption"); group.Add(note);
            foreach (var row in ((JObject)baseline["typeWeights"]).Properties())
                weights[row.Name] = Field("native-map-weight-"+row.Name,OriginalCardText.Humanize(row.Name)+" weight",0,(int)limits["maxWeight"],(int?)custom["mapShape"]?["typeWeights"]?[row.Name] ?? (int)row.Value);
            var reset = new Button(() => { floors.SetValueWithoutNotify(maximumFloors); columns.SetValueWithoutNotify(maximumColumns); foreach(var row in weights)row.Value.SetValueWithoutNotify((int)baseline["typeWeights"][row.Key]); Commit(); }) { text = "Reset map shape", name = "native-map-shape-reset" };
            reset.AddToClassList("action"); group.Add(reset);
            foreach (var control in controls) control.RegisterValueChangedCallback(_ => Commit());
            group.RegisterValueChangedCallback(_ => { Refresh(); report(); });
            Refresh();
            SliderInt Field(string id,string label,int min,int max,int value)
            {
                var field = new SliderInt(label,min,max) { name=id, value=value, showInputField=true };
                field.AddToClassList("foundation-field"); field.AddToClassList("map-shape-field"); field.style.minHeight=44;
                var textInput=field.Q<TextField>(); if(textInput!=null){textInput.name=id+"-input";textInput.style.minHeight=44;textInput.style.minWidth=56;}
                var numericInput=field.Q<IntegerField>(); if(numericInput!=null){numericInput.name=id+"-input";numericInput.style.minHeight=44;numericInput.style.minWidth=56;}
                controls.Add(field); group.Add(field); return field;
            }
            void Commit()
            {
                var shape = new JObject();
                if(floors.value<maximumFloors)shape["floors"]=floors.value;
                if(columns.value<maximumColumns)shape["columns"]=columns.value;
                var changedWeights=new JObject();foreach(var row in weights)if(row.Value.value!=(int)baseline["typeWeights"][row.Key])changedWeights[row.Key]=row.Value.value;
                if(changedWeights.HasValues)shape["typeWeights"]=changedWeights;
                if(shape.HasValues)custom["mapShape"]=shape;else custom.Remove("mapShape");
                Refresh();report();
            }
            void Refresh()
            {
                try
                {
                    var resolved=OriginalMapShape.ResolveAll(configs,custom["mapShape"],limits);var lines=new List<string>();var caveats=new HashSet<string>();
                    foreach(var row in resolved)
                    {
                        var config=(JObject)row["config"];
                        if(!group.value){lines.Add("Act "+row["act"]+": "+config["floors"]+" floors × "+config["columns"]+" columns.");continue;}
                        var cacheKey=config.ToString(Newtonsoft.Json.Formatting.None);
                        if(!samples.TryGetValue(cacheKey,out var sample)){sample=OriginalMapShape.Sample(config,seedCount);if(samples.Count>=64)samples.Clear();samples[cacheKey]=sample;}
                        lines.Add("Act "+row["act"]+": "+config["floors"]+" floors × "+config["columns"]+" columns; mean "+((double)sample["nodes"]["mean"]).ToString("0.##",CultureInfo.InvariantCulture)+" map nodes.");
                        foreach(var text in row["notes"].Values<string>())caveats.Add(text);
                        foreach(var type in new[]{"elite","merchant"})
                        {
                            var want=(int?)config["floorRules"][type=="elite"?"minElites":"minMerchants"]??0;var got=(double?)sample["byType"][type]??0;
                            if(got<want)caveats.Add("Small-map shortfall: "+type+" averages "+got.ToString("0.00",CultureInfo.InvariantCulture)+" of "+want+" promised per map.");
                        }
                    }
                    var total=weights.Values.Sum(x=>x.value);foreach(var row in weights)row.Value.label=OriginalCardText.Humanize(row.Key)+" weight · "+(total>0?Math.Floor(row.Value.value*100d/total+.5).ToString(CultureInfo.InvariantCulture)+"%":"—");
                    summary.text=string.Join("\n",lines)+(group.value?"\nMeasured across "+seedCount+" probe seeds; map nodes, not minutes.\n"+string.Join(" ",caveats):"\nOpen Run shape to sample map density.");
                    problem.text="";validityChanged?.Invoke(true);
                }
                catch(ArgumentException error){summary.text="Map shape cannot start.";problem.text=error.Message;validityChanged?.Invoke(false);}
            }
        }
    }
}
