#if VRC_SDK_VRCSDK3
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.RadialSlices;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.RadialPuppets.Base;

namespace VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.RadialPuppets
{
    public class TwoAxisPuppet : BaseAxisPuppet
    {
        public TwoAxisPuppet(RadialSliceControl control) : base(control)
        {
        }

        public override void Update(RadialCursor cursor)
        {
            if (!cursor.Get2Axis(Clamp, out var vector2)) return;

            Control.SetSubValue(0, vector2.x);
            Control.SetSubValue(1, vector2.y);
        }
    }
}
#endif