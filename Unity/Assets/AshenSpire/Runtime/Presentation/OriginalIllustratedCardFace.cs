// Native UI Toolkit renderer for the published Card Studio layer documents.
// Inputs are already resolved gameplay values. Geometry scales from the authored
// canvas; this view has no command, save, target selection or resource authority.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed class OriginalIllustratedCardFace : VisualElement
    {
        private static JObject _reference;
        private readonly JObject _document;
        private readonly List<(Label Label,JObject Layer)> _texts = new List<(Label,JObject)>();
        private float _lastWidth = -1;
        public static OriginalIllustratedCardFace Create(JObject card,JObject cost,string title,string rules,string tags)
        {
            if (_reference == null)
            {
                var asset = Resources.Load<TextAsset>("Original/illustrated-reference");
                if (asset == null) return null;
                var parsed = JObject.Parse(asset.text);
                if ((int?)parsed["referenceBuild"] != 898) throw new ArgumentException("Unknown illustrated card reference.");
                _reference = parsed;
            }
            return new OriginalIllustratedCardFace(card,cost,title,rules,tags);
        }
        private OriginalIllustratedCardFace(JObject card,JObject cost,string title,string rules,string tags)
        {
            AddToClassList("published-card-face"); pickingMode = PickingMode.Ignore;
            var id = (string)card["id"] ?? (string)card["cardId"];
            _document = (JObject)(_reference["documents"]["cards"]?[id] ?? _reference["documents"]["template"]).DeepClone();
            var layout = (int?)cost["mana"] > 0 ? "staminaMana" : "staminaOnly";
            var overrides = _document["costLayouts"]?[layout]?["layers"] as JObject;
            foreach (JObject original in _document["layers"])
            {
                var layer = (JObject)original.DeepClone();
                if (overrides?[(string)layer["id"]] is JObject patch)
                    foreach (var property in patch.Properties()) layer[property.Name] = property.Value.DeepClone();
                // Frozen pre-foundation runs still pay Actions. Keep that cost
                // visible until current rules replace it with Stamina payment.
                if (((string)layer["id"] == "energy-icon" || (string)layer["id"] == "energy-value")
                    && ((int?)cost["action"] > 0 || (bool?)cost["variable"] == true)) layer["visible"] = true;
                if ((bool?)layer["visible"] == false) continue;
                VisualElement view;
                if ((string)layer["type"] == "image")
                {
                    var resource = (string)layer["resource"];
                    if ((string)layer["bind"] == "artwork" && string.IsNullOrEmpty(resource))
                        resource = (string)_reference["profiles"]?[(string)card["equipmentProfileId"] ?? (string)card["profileId"] ?? ""]
                            ?? (string)_reference["artwork"]?[id]?["resource"];
                    var texture = string.IsNullOrEmpty(resource) ? null : Resources.Load<Texture2D>(resource);
                    if (texture == null) continue;
                    view = (bool?)layer["clip"] == true ? (VisualElement)new ClippedArtwork(texture,layer,_document)
                        : new Image { image = texture, scaleMode = (string)layer["fit"] == "contain" ? ScaleMode.ScaleToFit
                        : (string)layer["fit"] == "cover" ? ScaleMode.ScaleAndCrop : ScaleMode.StretchToFill };
                }
                else
                {
                    var binding = (string)layer["bind"];
                    var variable = (bool?)cost["variable"] == true;
                    var copy = binding == "name" ? title : binding == "rules" ? rules : binding == "tags" ? tags
                        : binding == "action" ? variable ? "X" : (string)cost["action"]
                        : binding == "stamina" ? variable ? "X" : (string)cost["stamina"]
                        : binding == "mana" ? (string)cost["mana"] : (string)layer["text"] ?? "";
                    var label = new Label(copy) { tooltip = copy };
                    label.style.whiteSpace = (int?)layer["maxLines"] == 1 ? WhiteSpace.NoWrap : WhiteSpace.Normal;
                    label.style.overflow = Overflow.Hidden;
                    label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
                    label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
                    if (ColorUtility.TryParseHtmlString((string)layer["color"],out var color)) label.style.color = color;
                    label.style.unityTextAlign = (string)layer["align"] == "left" ? TextAnchor.MiddleLeft
                        : (string)layer["align"] == "right" ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
                    label.style.unityFontStyleAndWeight = (string)layer["fontWeight"] == "bold" ? FontStyle.Bold : FontStyle.Normal;
                    label.style.unityFont = Resources.Load<Font>("Fonts/Cinzel-Regular");
                    _texts.Add((label,layer)); view = label;
                }
                view.name = "published-card-layer-" + layer["id"]; view.pickingMode = PickingMode.Ignore;
                view.style.position = Position.Absolute;
                view.style.left = Length.Percent((float)layer["x"] / (float)_document["width"] * 100);
                view.style.top = Length.Percent((float)layer["y"] / (float)_document["height"] * 100);
                view.style.width = Length.Percent((float)layer["w"] / (float)_document["width"] * 100);
                view.style.height = Length.Percent((float)layer["h"] / (float)_document["height"] * 100);
                view.style.opacity = (float?)layer["opacity"] ?? 1;
                view.style.rotate = new Rotate(new Angle((float?)layer["rotation"] ?? 0,AngleUnit.Degree));
                Add(view);
            }
            RegisterCallback<GeometryChangedEvent>(_ => Fit());
        }
        private void Fit()
        {
            var width = contentRect.width;
            if (width <= 0 || Mathf.Abs(width - _lastWidth) < .1f) return;
            _lastWidth = width; var scale = width / (float)_document["width"];
            style.height = (float)_document["height"] * scale;
            foreach (var text in _texts)
            {
                var maximum = ((float?)text.Layer["maxFontSize"] ?? (float?)text.Layer["fontSize"] ?? 20) * scale;
                var minimum = ((float?)text.Layer["minFontSize"] ?? (float?)text.Layer["fontSize"] ?? 20) * scale;
                var size = maximum;
                text.Label.style.fontSize = size;
                if ((bool?)text.Layer["autoFit"] == true)
                {
                    var availableWidth = (float)text.Layer["w"] * scale; var availableHeight = (float)text.Layer["h"] * scale;
                    for (var step = 0; step < 8 && size > minimum; step++)
                    {
                        var measured = text.Label.MeasureTextSize(text.Label.text,availableWidth,MeasureMode.AtMost,0,MeasureMode.Undefined);
                        if (measured.y <= availableHeight + 1 && measured.x <= availableWidth + 1) break;
                        size = Mathf.Max(minimum,size - (maximum-minimum)/7); text.Label.style.fontSize = size;
                    }
                }
            }
        }
        // UI Toolkit has no CSS clip-path. Draw the same convex authored card
        // polygon as a textured mesh, intersected with the artwork's fitted box.
        private sealed class ClippedArtwork : VisualElement
        {
            private readonly Texture2D _texture;
            private readonly JObject _layer, _document;
            public ClippedArtwork(Texture2D texture,JObject layer,JObject document)
            { _texture=texture;_layer=layer;_document=document;generateVisualContent+=Paint;RegisterCallback<GeometryChangedEvent>(_=>MarkDirtyRepaint()); }
            private static List<Vector2> Cut(List<Vector2> input,Func<Vector2,bool> inside,Func<Vector2,Vector2,Vector2> intersect)
            {
                var output=new List<Vector2>();if(input.Count==0)return output;
                var previous=input[input.Count-1];var previousInside=inside(previous);
                foreach(var point in input)
                {
                    var pointInside=inside(point);
                    if(pointInside!=previousInside)output.Add(intersect(previous,point));
                    if(pointInside)output.Add(point);
                    previous=point;previousInside=pointInside;
                }
                return output;
            }
            private void Paint(MeshGenerationContext context)
            {
                if(contentRect.width<=0||contentRect.height<=0)return;
                var x=(float)_layer["x"];var y=(float)_layer["y"];var w=(float)_layer["w"];var h=(float)_layer["h"];
                var box=new Rect(x,y,w,h);var image=box;
                var fit=(string)_layer["fit"];
                if(fit=="contain"||fit=="cover")
                {
                    var scale=fit=="contain"?Mathf.Min(w/_texture.width,h/_texture.height):Mathf.Max(w/_texture.width,h/_texture.height);
                    image=new Rect(x+(w-_texture.width*scale)/2,y+(h-_texture.height*scale)/2,_texture.width*scale,_texture.height*scale);
                }
                var clip=new List<Vector2>();var dw=(float)_document["width"];var dh=(float)_document["height"];
                if(_document["clipPolygon"] is JArray points)
                    foreach(var point in points)clip.Add(new Vector2((float)point[0]*dw,(float)point[1]*dh));
                else
                    clip.AddRange(new[]{new Vector2(.12f*dw,.04f*dh),new Vector2(.88f*dw,.04f*dh),new Vector2(.95f*dw,.11f*dh),new Vector2(.95f*dw,.9f*dh),new Vector2(.88f*dw,.966f*dh),new Vector2(.12f*dw,.966f*dh),new Vector2(.052f*dw,.9f*dh),new Vector2(.052f*dw,.11f*dh)});
                var left=Mathf.Max(box.xMin,image.xMin);var right=Mathf.Min(box.xMax,image.xMax);var top=Mathf.Max(box.yMin,image.yMin);var bottom=Mathf.Min(box.yMax,image.yMax);
                Vector2 AtX(Vector2 a,Vector2 b,float edge)=>new Vector2(edge,a.y+(b.y-a.y)*(edge-a.x)/(b.x-a.x));
                Vector2 AtY(Vector2 a,Vector2 b,float edge)=>new Vector2(a.x+(b.x-a.x)*(edge-a.y)/(b.y-a.y),edge);
                clip=Cut(clip,p=>p.x>=left,(a,b)=>AtX(a,b,left));clip=Cut(clip,p=>p.x<=right,(a,b)=>AtX(a,b,right));
                clip=Cut(clip,p=>p.y>=top,(a,b)=>AtY(a,b,top));clip=Cut(clip,p=>p.y<=bottom,(a,b)=>AtY(a,b,bottom));
                if(clip.Count<3)return;
                var mesh=context.Allocate(clip.Count,(clip.Count-2)*3,_texture);
#if UNITY_6000_0_OR_NEWER
                // Unity 6 remaps normalized texture UVs into its atlas itself.
                var region=new Rect(0,0,1,1);
#else
                var region=mesh.uvRegion;
#endif
                foreach(var point in clip)
                    mesh.SetNextVertex(new Vertex { position=new Vector3((point.x-x)*contentRect.width/w,(point.y-y)*contentRect.height/h,Vertex.nearZ),tint=Color.white,
                        uv=new Vector2(region.xMin+(point.x-image.xMin)/image.width*region.width,region.yMin+(1-(point.y-image.yMin)/image.height)*region.height) });
                for(var i=1;i<clip.Count-1;i++){mesh.SetNextIndex(0);mesh.SetNextIndex((ushort)i);mesh.SetNextIndex((ushort)(i+1));}
            }
        }
    }
}
