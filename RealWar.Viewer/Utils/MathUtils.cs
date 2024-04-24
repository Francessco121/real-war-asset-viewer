using System;

namespace RealWar.Viewer.Utils;

public static class MathUtils
{
    public static float ToRadians(float degrees)
    {
        return degrees * (MathF.PI / 180);
    }
}
