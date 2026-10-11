using System;
using System.Drawing;

namespace FrameSpool
{
    // Recovered arithmetic, independently implemented. EPCOLOR2015.dll:
    // RGB->YCC FUN_10010b50; YCC->RGB FUN_10010cd0/table 1006d024.
    // These conversions alone do not reproduce PhotoEnhance, APF, PIM or ICC.
    public static class FixedPointColorMath
    {
        public static byte[] ToYcc(Color rgb)
        {
            int y=(313525*rgb.R+615512*rgb.G+119538*rgb.B)>>20;
            int cb=(-176936*rgb.R-347351*rgb.G+524286*rgb.B)>>20;
            int cr=(524286*rgb.R-439028*rgb.G-85259*rgb.B)>>20;
            return new byte[]{(byte)Clamp(y,0,255),(byte)(Clamp(cb,-128,127)+128),(byte)(Clamp(cr,-128,127)+128)};
        }
        public static Color FromYcc(byte y,byte cb,byte cr)
        {
            int b=cb-128,r=cr-128;
            return Color.FromArgb(Clamp((1048576*y+1470103*r)>>20,0,255),Clamp((1048576*y-360856*b-748830*r)>>20,0,255),Clamp((1048576*y+1858076*b)>>20,0,255));
        }
        static int Clamp(int value,int min,int max){return Math.Max(min,Math.Min(max,value));}
    }
}
