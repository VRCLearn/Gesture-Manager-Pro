#if VRC_SDK_VRCSDK3
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.RadialSlices;
using VRCLearn.GestureManagerPro.Library.VisualElements;

namespace VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.RadialPuppets.Base
{
    public abstract class BasePuppet : GmgCircleElement
    {
        internal readonly RadialSliceControl Control;
        internal abstract float Clamp { get; }

        protected BasePuppet(float size, RadialSliceControl control)
        {
            Control = control;
            RadialMenuUtility.Prefabs.SetCircle(this, size, RadialMenuUtility.Colors.CustomMain, RadialMenuUtility.Colors.CustomBorder);
        }

        public virtual void ReloadColors()
        {
            VertexColor = RadialMenuUtility.Colors.CustomMain;
            CenterColor = RadialMenuUtility.Colors.CustomMain;
            BorderColor = RadialMenuUtility.Colors.CustomBorder;
        }

        public void OnOpen() => Control.SetControlValue();

        public void OnClose() => Control.SetValue(0);

        public abstract void UpdateValue(string pName, float value);

        public abstract void Update(RadialCursor cursor);

        public abstract void AfterCursor();
    }
}
#endif