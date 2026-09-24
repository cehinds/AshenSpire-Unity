// Compile-only stand-ins for UI Toolkit types that exist in Unity 6 but not in the
// UnityEngine.Modules 2021.3 reference package. Never shipped; never executed.
namespace UnityEngine.UIElements
{
    public enum LineJoin { Miter, Bevel, Round }
    public enum LineCap { Butt, Round }
    public class IntegerField : BaseField<int>
    {
        public IntegerField() : base(null, null) { }
        public IntegerField(string label) : base(label, null) { }
    }
}
