using AshenSpire.Presentation;
var passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS: "+name);}
foreach(var height in new[]{320,390,599,600,740,844,900,1080})
{
    var ui=ViewportLayout.MinimumTouchHeight(height);
    var displayed=ui*height/ViewportLayout.ReferenceHeight(height);
    Check(displayed>=44 && displayed<44+(float)height/ViewportLayout.ReferenceHeight(height),$"{height}: 44 displayed pixels with less than one UI point of rounding");
}
Check(ViewportLayout.ReferenceHeight(599)==599,"compact landscape uses displayed height");
Check(ViewportLayout.ReferenceHeight(600)==900,"portrait threshold remains explicit");
Check(ViewportLayout.MinimumTouchHeight(740)==54,"narrow portrait requires 54 UI points, not the old 50-point floor");
Check(ViewportLayout.MinimumTouchHeight(320)==44,"dense landscape retains 44 displayed pixels");
Check(float.IsFinite(ViewportLayout.MinimumTouchHeight(0)),"zero-size initialization remains finite");
Check(float.IsFinite(ViewportLayout.MinimumTouchHeight(-1)),"unavailable height remains finite");
Console.WriteLine($"Viewport: {passed} checks passed");
